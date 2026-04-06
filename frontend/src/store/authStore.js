/**
 * Auth Store - Zustand
 * Manages authentication state with secure token strategy
 * 
 * ✅ CRIT-4: Token Storage Strategy (XSS-hardened):
 * - accessToken: MEMORY-ONLY (Zustand state) — ephemeral, cleared on page close
 * - refreshToken: localStorage (for session persistence + silent refresh on reload)
 * 
 * On page reload: refreshToken is used to obtain a new accessToken via silent refresh.
 * This eliminates the XSS attack surface for accessToken theft.
 */
import { create } from 'zustand';
import axiosInstance, { setAuthStoreGetter } from '../api/axios';
import { safeSetItem } from '../utils/storageUtils';
import { TOKEN_REFRESH_BUFFER_MS, ACCESS_TOKEN_EXPIRY_MS, SILENT_REFRESH_INTERVAL_MS, API_ENDPOINTS } from '../constants';

// LocalStorage keys with prefix
const STORAGE_KEYS = {
  REFRESH_TOKEN: 'tts_refresh_token',
};

/**
 * Create the auth store
 * Note: We use localStorage for both tokens to simplify persistence
 */
const useAuthStore = create((set, get) => ({
  // State — accessToken is MEMORY-ONLY (CRIT-4)
  accessToken: null,
  refreshToken: localStorage.getItem(STORAGE_KEYS.REFRESH_TOKEN),
  user: null,
  isAuthenticated: false,
  isLoading: false,

  // Silent refresh timer
  refreshTimer: null,

  // Actions
  setToken: (accessToken, refreshToken) => {
    // ✅ CRIT-4: accessToken stays in memory only — NOT in localStorage
    if (refreshToken) {
      safeSetItem(STORAGE_KEYS.REFRESH_TOKEN, refreshToken);
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

      // ✅ CRIT-4: Only refreshToken goes to localStorage
      if (refreshToken) {
        safeSetItem(STORAGE_KEYS.REFRESH_TOKEN, refreshToken);
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

  loginWithGoogle: async (idToken) => {
    set({ isLoading: true });
    try {
      const response = await axiosInstance.post(API_ENDPOINTS.AUTH.GOOGLE_LOGIN, { idToken });
      const data = response.data;
      const { accessToken, refreshToken, email: user } = data;

      // ✅ CRIT-4: Only refreshToken goes to localStorage
      if (refreshToken) {
        safeSetItem(STORAGE_KEYS.REFRESH_TOKEN, refreshToken);
      }

      set({
        accessToken,
        refreshToken,
        user,
        isAuthenticated: true,
        isLoading: false,
      });

      get().startSilentRefresh();
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
      // Clear timer
      if (get().refreshTimer) {
        clearInterval(get().refreshTimer);
      }

      // Clear localStorage (only refreshToken is stored there)
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

    // Clear localStorage (only refreshToken is stored there)
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

        // ✅ CRIT-4: Only refreshToken goes to localStorage
        if (newRefreshToken) {
          safeSetItem(STORAGE_KEYS.REFRESH_TOKEN, newRefreshToken);
        }

        set({ accessToken, refreshToken: newRefreshToken });
      } catch (error) {
        console.error('Silent refresh failed:', error);
        get().forceLogout();
      }
    }, SILENT_REFRESH_INTERVAL_MS);

    set({ refreshTimer: timer });
  },

  // ✅ CRIT-4: Initialize auth by doing a silent refresh (accessToken is memory-only)
  initializeAuth: async () => {
    const refreshToken = localStorage.getItem(STORAGE_KEYS.REFRESH_TOKEN);

    if (refreshToken) {
      try {
        // Obtain a fresh accessToken using the stored refreshToken
        const response = await axiosInstance.post(API_ENDPOINTS.AUTH.REFRESH, { refreshToken });
        const { accessToken, refreshToken: newRefreshToken } = response.data;

        if (newRefreshToken) {
          safeSetItem(STORAGE_KEYS.REFRESH_TOKEN, newRefreshToken);
        }

        set({
          accessToken,
          refreshToken: newRefreshToken || refreshToken,
          isAuthenticated: true,
        });

        // Start silent refresh timer
        get().startSilentRefresh();
      } catch (error) {
        console.warn('Silent refresh on init failed, clearing session:', error);
        localStorage.removeItem(STORAGE_KEYS.REFRESH_TOKEN);
        set({ accessToken: null, refreshToken: null, isAuthenticated: false });
      }
    }
  },
}));

// Listen for logout events from axios interceptor
if (typeof window !== 'undefined') {
  window.addEventListener('auth:logout', () => {
    useAuthStore.getState().forceLogout();
  });
}

// Export store getter for axios integration (avoid circular dependency)
export const getAuthStoreState = () => useAuthStore.getState();

export default useAuthStore;