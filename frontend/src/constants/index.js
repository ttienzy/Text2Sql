// API Configuration
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'https://localhost:7189';
export const APP_NAME = import.meta.env.VITE_APP_NAME || 'TextToSQL Agent';
export const TOKEN_REFRESH_BUFFER_MS = parseInt(import.meta.env.VITE_TOKEN_REFRESH_BUFFER_MS || '60000', 10);

// Token expiration times (in milliseconds)
export const ACCESS_TOKEN_EXPIRY_MS = 3 * 60 * 60 * 1000; // 3 hours
export const SILENT_REFRESH_INTERVAL_MS = 179 * 60 * 1000; // 179 minutes (15 min before expiry)

// API Endpoints
export const API_ENDPOINTS = {
  AUTH: {
    LOGIN: '/api/auth/login',
    REGISTER: '/api/auth/register',
    REFRESH: '/api/auth/refresh',
    LOGOUT: '/api/auth/logout',
  },
  CONNECTIONS: '/api/connections',
  CONVERSATIONS: '/api/conversations',
  MESSAGES: '/api/messages',
  AGENT: '/api/agent',
  JOBS: '/api/jobs',
};

// HTTP Status Codes
export const HTTP_STATUS = {
  OK: 200,
  CREATED: 201,
  BAD_REQUEST: 400,
  UNAUTHORIZED: 401,
  FORBIDDEN: 403,
  NOT_FOUND: 404,
  INTERNAL_SERVER_ERROR: 500,
};

// Storage Keys (for non-sensitive data only)
export const STORAGE_KEYS = {
  THEME: 'app_theme',
  LANGUAGE: 'app_language',
};
