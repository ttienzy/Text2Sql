import axios from 'axios';
import { API_BASE_URL, HTTP_STATUS } from '../constants';
import { extractErrorMessage, createError } from '../utils/errorHandler';

// LocalStorage keys (must match authStore.js)
const STORAGE_KEYS = {
  ACCESS_TOKEN: 'tts_access_token',
  REFRESH_TOKEN: 'tts_refresh_token',
};

// Event emitter for global error handling
const errorEventEmitter = new EventTarget();

/**
 * Dispatch a global error event
 * @param {Object} error - The error object
 */
export const dispatchGlobalError = (error) => {
  const standardizedError = createError(error);
  errorEventEmitter.dispatchEvent(new CustomEvent('api:error', {
    detail: standardizedError
  }));
};

/**
 * Subscribe to global errors
 * @param {Function} handler - Error handler function
 * @returns {Function} - Unsubscribe function
 */
export const subscribeToGlobalErrors = (handler) => {
  const handlerWrapper = (event) => handler(event.detail);
  errorEventEmitter.addEventListener('api:error', handlerWrapper);
  return () => errorEventEmitter.removeEventListener('api:error', handlerWrapper);
};

// Create axios instance
const axiosInstance = axios.create({
  baseURL: API_BASE_URL,
  timeout: 300000, // 5 minute timeout for long-running requests
  headers: {
    'Content-Type': 'application/json',
  },
});

// Store for managing token refresh
let refreshTokenPromise = null;

// Request interceptor - attach Bearer token from localStorage
axiosInstance.interceptors.request.use(
  (config) => {
    // Get token from localStorage
    const token = localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN);

    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }

    return config;
  },
  (error) => {
    return Promise.reject(error);
  }
);

// Response interceptor - handle 401 and token refresh
axiosInstance.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    // Standardize error before processing
    const standardizedError = {
      ...error,
      message: extractErrorMessage(error),
    };

    // If 401 error and haven't tried to refresh yet
    if (error.response?.status === HTTP_STATUS.UNAUTHORIZED && !originalRequest._retry) {
      originalRequest._retry = true;

      // Add retry counter to prevent infinite loops
      originalRequest._refreshRetryCount = (originalRequest._refreshRetryCount || 0) + 1;

      if (originalRequest._refreshRetryCount > 1) {
        console.error('❌ Max refresh retry limit reached (1 retry allowed)');
        window.dispatchEvent(new CustomEvent('auth:logout', { detail: { reason: 'max_retry_exceeded' } }));
        return Promise.reject(standardizedError);
      }

      try {
        // If already refreshing, wait for that promise
        if (refreshTokenPromise) {
          console.log('🔄 Waiting for existing refresh token promise...');
          await refreshTokenPromise;

          // Get the new token from localStorage
          const newToken = localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN);
          if (newToken) {
            originalRequest.headers.Authorization = `Bearer ${newToken}`;
            return axiosInstance(originalRequest);
          } else {
            throw new Error('No token after refresh');
          }
        }

        // Start refresh process
        const refreshToken = localStorage.getItem(STORAGE_KEYS.REFRESH_TOKEN);

        if (!refreshToken) {
          // No refresh token, force logout
          console.log('❌ No refresh token available, forcing logout');
          window.dispatchEvent(new CustomEvent('auth:logout', { detail: { reason: 'token_expired' } }));
          return Promise.reject(standardizedError);
        }

        // Create refresh promise with better error handling
        refreshTokenPromise = (async () => {
          console.log('🔄 Starting token refresh...');
          try {
            const response = await axios.post(`${API_BASE_URL}/api/auth/refresh`, {
              refreshToken, // camelCase to match backend DTO
            });

            const { accessToken, refreshToken: newRefreshToken } = response.data;

            // Update localStorage directly
            localStorage.setItem(STORAGE_KEYS.ACCESS_TOKEN, accessToken);
            if (newRefreshToken) {
              localStorage.setItem(STORAGE_KEYS.REFRESH_TOKEN, newRefreshToken);
            }

            console.log('✅ Token refresh successful');
            return accessToken;
          } catch (refreshError) {
            console.error('❌ Token refresh failed:', refreshError);

            // Clear both tokens and force logout
            localStorage.removeItem(STORAGE_KEYS.ACCESS_TOKEN);
            localStorage.removeItem(STORAGE_KEYS.REFRESH_TOKEN);

            window.dispatchEvent(new CustomEvent('auth:logout', {
              detail: { reason: 'refresh_failed' }
            }));

            throw refreshError;
          } finally {
            refreshTokenPromise = null;
          }
        })();

        // Wait for refresh and retry original request
        await refreshTokenPromise;

        // Get the new token from localStorage
        const newAccessToken = localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN);
        if (!newAccessToken) {
          throw new Error('No access token after refresh');
        }

        // Update the original request with new token
        originalRequest.headers.Authorization = `Bearer ${newAccessToken}`;

        console.log('🔄 Retrying original request with new token');
        return axiosInstance(originalRequest);

      } catch (refreshError) {
        console.error('❌ Token refresh process failed:', refreshError);
        return Promise.reject({
          ...standardizedError,
          message: extractErrorMessage(refreshError),
        });
      }
    }

    // Dispatch global error event for non-401 errors
    dispatchGlobalError(error);

    return Promise.reject(standardizedError);
  }
);

export default axiosInstance;

// Export error utilities for use in components
export { extractErrorMessage, createError };
