/**
 * Auth Store - Zustand
 * Manages authentication state with localStorage token strategy
 * 
 * Token Storage Strategy:
 * - accessToken: localStorage (for persistence across page reloads)
 * - refreshToken: localStorage (for session persistence)
 * 
 * Note: Both tokens stored in localStorage for simplicity.
 * For high-security applications, consider memory-only accessToken.
 */
import { create } from 'zustand';
import axiosInstance from '../api/axios';
import { safeSetItem } from '../utils/storageUtils';
import { API_ENDPOINTS } from '../constants';

// LocalStorage keys with prefix
const STORAGE_KEYS = {
  ACCESS_TOKEN: 'tts_access_token',
  REFRESH_TOKEN: 'tts_refresh_token',
};

/**
 * Create the auth store
 */
const useAuthStore = create((set, get) => ({
  // State - Both tokens from localStorage
  accessToken: localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN),
  refreshToken: localStorage.getItem(STORAGE_KEYS.REFRESH_TOKEN),
  user: null,
  isAuthenticated: !!localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN),
  isLoading: false,

  // Actions
  setToken: (accessToken, refreshToken) => {
    // Store both tokens in localStorage
    if (accessToken) {
      safeSetItem(STORAGE_KEYS.ACCESS_TOKEN, accessToken);
    }
    if (refreshToken) {
      safeSetItem(STORAGE_KEYS.REFRESH_TOKEN, refreshToken);
    }

    set({
      accessToken,
      refreshToken,
      isAuthenticated: !!accessToken
    });
  },

  setUser: (user) => {
    set({ user });
  },

  login: async (credentials) => {
    set({ isLoading: true });
    try {
      const response = await axiosInstance.post(API_ENDPOINTS.AUTH.LOGIN, credentials);
      const data = response.data;

      // API returns camelCase: accessToken, refreshToken, email, fullName, avatarUrl
      const { accessToken, refreshToken, ...user } = data;

      // Store both tokens in localStorage
      get().setToken(accessToken, refreshToken);

      set({
        user,
        isLoading: false,
      });
    } catch (error) {
      set({ isLoading: false });
      throw error;
    }
  },

  loginWithGoogle: async (idToken) => {
    set({ isLoading: true });
    try {
      const response = await axiosInstance.post(API_ENDPOINTS.AUTH.GOOGLE_LOGIN, { idToken });
      const data = response.data;
      const { accessToken, refreshToken, ...user } = data;

      // Store both tokens in localStorage
      get().setToken(accessToken, refreshToken);

      set({
        user,
        isLoading: false,
      });
    } catch (error) {
      set({ isLoading: false });
      throw error;
    }
  },

  forgotPassword: async (email) => {
    set({ isLoading: true });
    try {
      await axiosInstance.post(API_ENDPOINTS.AUTH.FORGOT_PASSWORD, { email });
      set({ isLoading: false });
    } catch (error) {
      set({ isLoading: false });
      throw error;
    }
  },

  resetPassword: async (email, code, newPassword) => {
    set({ isLoading: true });
    try {
      await axiosInstance.post(API_ENDPOINTS.AUTH.RESET_PASSWORD, { email, code, newPassword });
      set({ isLoading: false });
    } catch (error) {
      set({ isLoading: false });
      throw error;
    }
  },

  fetchProfile: async () => {
    try {
      const response = await axiosInstance.get(API_ENDPOINTS.AUTH.PROFILE);
      return response.data;
    } catch (error) {
      console.error('Fetch profile failed:', error);
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
      // Clear both tokens from localStorage
      localStorage.removeItem(STORAGE_KEYS.ACCESS_TOKEN);
      localStorage.removeItem(STORAGE_KEYS.REFRESH_TOKEN);

      set({
        accessToken: null,
        refreshToken: null,
        user: null,
        isAuthenticated: false,
      });
    }
  },

  forceLogout: () => {
    // Clear both tokens from localStorage
    localStorage.removeItem(STORAGE_KEYS.ACCESS_TOKEN);
    localStorage.removeItem(STORAGE_KEYS.REFRESH_TOKEN);

    set({
      accessToken: null,
      refreshToken: null,
      user: null,
      isAuthenticated: false,
    });

    // Redirect to login
    window.location.href = '/login';
  },

  // Initialize auth from localStorage (no API call needed)
  initializeAuth: async () => {
    const accessToken = localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN);
    const refreshToken = localStorage.getItem(STORAGE_KEYS.REFRESH_TOKEN);

    if (accessToken && refreshToken) {
      set({
        accessToken,
        refreshToken,
        isAuthenticated: true,
      });

      // Hydrate user info from API
      try {
        const userProfile = await get().fetchProfile();
        set({ user: userProfile });
      } catch (error) {
        console.error('Failed to restore user session:', error);
      }
    } else {
      // Clear any partial tokens
      localStorage.removeItem(STORAGE_KEYS.ACCESS_TOKEN);
      localStorage.removeItem(STORAGE_KEYS.REFRESH_TOKEN);
      set({
        accessToken: null,
        refreshToken: null,
        isAuthenticated: false,
      });
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