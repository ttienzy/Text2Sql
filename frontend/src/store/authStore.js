/**
 * Auth Store - Zustand
 * Manages authentication state with localStorage for persistence
 * 
 * Token Storage Strategy:
 * - accessToken: localStorage (for session persistence across tabs)
 * - refreshToken: localStorage (for automatic refresh)
 * 
 * Security Note:
 * - Using localStorage for accessToken is acceptable for this use case
 * - Alternative: memory-only storage requires more complex initialization
 */
import { create } from 'zustand';
import axiosInstance from '../api/axios';
import { TOKEN_REFRESH_BUFFER_MS, ACCESS_TOKEN_EXPIRY_MS, SILENT_REFRESH_INTERVAL_MS, API_ENDPOINTS } from '../constants';

// LocalStorage keys with prefix
const STORAGE_KEYS = {
  ACCESS_TOKEN: 'tts_access_token',
  REFRESH_TOKEN: 'tts_refresh_token',
};

/**
 * Create the auth store
 * Note: We use localStorage for both tokens to simplify persistence
 */
const useAuthStore = create((set, get) => ({
  // State
  accessToken: localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN),
  refreshToken: localStorage.getItem(STORAGE_KEYS.REFRESH_TOKEN),
  user: null,
  isAuthenticated: !!localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN),
  isLoading: false,

  // Silent refresh timer
  refreshTimer: null,

  // Actions
  setToken: (accessToken, refreshToken) => {
    // Store both tokens in localStorage
    if (accessToken) {
      localStorage.setItem(STORAGE_KEYS.ACCESS_TOKEN, accessToken);
    }
    if (refreshToken) {
      localStorage.setItem(STORAGE_KEYS.REFRESH_TOKEN, refreshToken);
    }

    set({
      accessToken,
      refreshToken,
      isAuthenticated: !!accessToken
    });

    // Start silent refresh timer
    get().startSilentRefresh();
  },

  setUser: (user) => {
    set({ user });
  },

  login: async (credentials) => {
    set({ isLoading: true });
    try {
      const response = await axiosInstance.post(API_ENDPOINTS.AUTH.LOGIN, credentials);
      const data = response.data;

      // API returns camelCase: accessToken, refreshToken
      const { accessToken, refreshToken, email: user } = data;

      // Store both tokens in localStorage
      localStorage.setItem(STORAGE_KEYS.ACCESS_TOKEN, accessToken);
      if (refreshToken) {
        localStorage.setItem(STORAGE_KEYS.REFRESH_TOKEN, refreshToken);
      }

      set({
        accessToken,
        refreshToken,
        user,
        isAuthenticated: true,
        isLoading: false,
      });

      // Start silent refresh timer
      get().startSilentRefresh();
    } catch (error) {
      set({ isLoading: false });
      throw error;
    }
  },

  logout: async () => {
    try {
      // Call logout API to revoke refresh token
      const refreshToken = get().refreshToken;
      if (refreshToken) {
        await axiosInstance.post(API_ENDPOINTS.AUTH.LOGOUT, { refreshToken });
      }
    } catch (error) {
      // Continue with logout even if API call fails
      console.warn('Logout API call failed:', error);
    } finally {
      // Clear timer
      if (get().refreshTimer) {
        clearInterval(get().refreshTimer);
      }

      // Clear localStorage
      localStorage.removeItem(STORAGE_KEYS.ACCESS_TOKEN);
      localStorage.removeItem(STORAGE_KEYS.REFRESH_TOKEN);

      set({
        accessToken: null,
        refreshToken: null,
        user: null,
        isAuthenticated: false,
        refreshTimer: null,
      });
    }
  },

  forceLogout: () => {
    // Clear timer
    if (get().refreshTimer) {
      clearInterval(get().refreshTimer);
    }

    // Clear localStorage
    localStorage.removeItem(STORAGE_KEYS.ACCESS_TOKEN);
    localStorage.removeItem(STORAGE_KEYS.REFRESH_TOKEN);

    set({
      accessToken: null,
      refreshToken: null,
      user: null,
      isAuthenticated: false,
      refreshTimer: null,
    });

    // Redirect to login
    window.location.href = '/login';
  },

  // Silent refresh timer - triggers periodically to refresh token before expiry
  startSilentRefresh: () => {
    // Clear existing timer
    if (get().refreshTimer) {
      clearInterval(get().refreshTimer);
    }

    const timer = setInterval(async () => {
      const refreshToken = localStorage.getItem(STORAGE_KEYS.REFRESH_TOKEN);
      if (!refreshToken) {
        get().forceLogout();
        return;
      }

      try {
        const response = await axiosInstance.post(API_ENDPOINTS.AUTH.REFRESH, { refreshToken });
        const data = response.data;
        const { accessToken, refreshToken: newRefreshToken } = data;

        // Update localStorage
        localStorage.setItem(STORAGE_KEYS.ACCESS_TOKEN, accessToken);
        if (newRefreshToken) {
          localStorage.setItem(STORAGE_KEYS.REFRESH_TOKEN, newRefreshToken);
        }

        set({ accessToken, refreshToken: newRefreshToken });
      } catch (error) {
        console.error('Silent refresh failed:', error);
        get().forceLogout();
      }
    }, SILENT_REFRESH_INTERVAL_MS);

    set({ refreshTimer: timer });
  },

  // Initialize auth state from localStorage
  // Note: We don't call refresh API here - just use existing tokens
  initializeAuth: () => {
    const accessToken = localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN);
    const refreshToken = localStorage.getItem(STORAGE_KEYS.REFRESH_TOKEN);

    if (accessToken && refreshToken) {
      set({
        accessToken,
        refreshToken,
        isAuthenticated: true,
      });

      // Start silent refresh timer
      get().startSilentRefresh();
    }
  },
}));

// Listen for logout events from axios interceptor
if (typeof window !== 'undefined') {
  window.addEventListener('auth:logout', () => {
    useAuthStore.getState().forceLogout();
  });
}

export default useAuthStore;