/**
 * Unified Error Handling Utility
 * Provides consistent error message extraction across the application
 */

/**
 * Error severity levels
 * @readonly
 * @enum {string}
 */
export const ErrorSeverity = {
  INFO: 'info',
  WARNING: 'warning',
  ERROR: 'error',
  CRITICAL: 'critical',
};

/**
 * Extracts normalized error message from various error response formats
 * Handles: errorMessage, ErrorMessage, message, response.data, axios errors
 * 
 * @param {any} error - The error object (axios error, custom error, or plain object)
 * @returns {string} - Normalized error message string
 */
export const extractErrorMessage = (error) => {
  if (!error) return 'An unknown error occurred';
  
  // Handle string errors
  if (typeof error === 'string') return error;
  
  // Handle Error objects
  if (error instanceof Error) {
    return error.message || 'An error occurred';
  }
  
  // Handle axios error response
  if (error.response) {
    const { data, status } = error.response;
    
    // Try different error message fields (case-insensitive check)
    if (data) {
      // Check for common error message fields
      if (data.errorMessage) return data.errorMessage;
      if (data.ErrorMessage) return data.ErrorMessage;
      if (data.message) return data.message;
      if (data.Message) return data.Message;
      if (data.error) return typeof data.error === 'string' ? data.error : data.error.message;
      
      // Handle validation errors (often in a validation object)
      if (data.errors) {
        const errors = data.errors;
        if (Array.isArray(errors) && errors.length > 0) {
          return errors.map(e => e.message || e).join(', ');
        }
        if (typeof errors === 'object') {
          return Object.values(errors).flat().join(', ');
        }
      }
      
      // Handle Problem Details (RFC 7807)
      if (data.title) return data.title;
      if (data.detail) return data.detail;
    }
    
    // HTTP status-based messages
    switch (status) {
      case 400:
        return 'Bad request. Please check your input.';
      case 401:
        return 'Unauthorized. Please log in again.';
      case 403:
        return 'Access denied. You do not have permission.';
      case 404:
        return 'Resource not found.';
      case 408:
        return 'Request timeout. Please try again.';
      case 422:
        return 'Validation failed. Please check your input.';
      case 429:
        return 'Too many requests. Please wait a moment.';
      case 500:
        return 'Server error. Please try again later.';
      case 502:
        return 'Service unavailable. Please try again later.';
      case 503:
        return 'Service temporarily unavailable.';
      default:
        return `Request failed with status ${status}`;
    }
  }
  
  // Handle request errors (no response)
  if (error.request) {
    if (error.code === 'ECONNABORTED') {
      return 'Request timeout. Please check your connection and try again.';
    }
    if (error.code === 'NETWORK_ERROR' || error.message === 'Network Error') {
      return 'Network error. Please check your internet connection.';
    }
    return 'Unable to connect to server. Please try again.';
  }
  
  // Handle custom error objects with errorMessage field
  if (error.errorMessage) return error.errorMessage;
  if (error.ErrorMessage) return error.ErrorMessage;
  if (error.message) return error.message;
  if (error.Message) return error.Message;
  
  // Fallback
  return 'An unexpected error occurred';
};

/**
 * Checks if an error object has any error message
 * 
 * @param {any} error - The error object to check
 * @returns {boolean} - True if error has a message
 */
export const hasError = (error) => {
  if (!error) return false;
  if (typeof error === 'string') return error.trim().length > 0;
  if (error instanceof Error) return !!error.message;
  
  return !!(error.errorMessage || error.ErrorMessage || error.message || error.Message);
};

/**
 * Creates a standardized error object
 * 
 * @param {string|Error|Object} errorSource - The source of the error
 * @param {string} [fallbackMessage='An error occurred'] - Fallback message if no error message found
 * @returns {Object} - Standardized error object
 */
export const createError = (errorSource, fallbackMessage = 'An error occurred') => {
  const message = extractErrorMessage(errorSource) || fallbackMessage;
  
  return {
    message,
    original: errorSource,
    timestamp: new Date().toISOString(),
    // Determine severity based on error type
    severity: determineSeverity(errorSource),
  };
};

/**
 * Determines the severity level of an error
 * 
 * @param {any} error - The error object
 * @returns {string} - Error severity level
 */
const determineSeverity = (error) => {
  if (!error) return ErrorSeverity.ERROR;
  
  // Check for explicit severity field
  if (error.severity) return error.severity;
  
  // Check HTTP status for severity
  if (error.response?.status) {
    const status = error.response.status;
    if (status >= 500) return ErrorSeverity.CRITICAL;
    if (status === 429) return ErrorSeverity.WARNING;
    if (status >= 400 && status < 500) return ErrorSeverity.ERROR;
  }
  
  // Check for network errors
  if (error.code === 'NETWORK_ERROR' || error.message === 'Network Error') {
    return ErrorSeverity.WARNING;
  }
  
  return ErrorSeverity.ERROR;
};

/**
 * Formats error for display in UI
 * 
 * @param {string|Error|Object} error - The error to format
 * @param {Object} options - Display options
 * @param {boolean} options.showIcon - Whether to show error icon
 * @param {boolean} options.showRetry - Whether to show retry suggestion
 * @returns {Object} - Formatted error display object
 */
export const formatErrorForDisplay = (error, options = {}) => {
  const { showIcon = true, showRetry = true } = options;
  const message = extractErrorMessage(error);
  
  let suggestion = '';
  if (showRetry) {
    const originalError = error?.response?.status;
    if (originalError === 429) {
      suggestion = 'Please wait a moment before trying again.';
    } else if (originalError >= 500 || !error.response) {
      suggestion = 'Please try again later.';
    } else {
      suggestion = 'Please check your input and try again.';
    }
  }
  
  return {
    title: 'Error',
    message,
    suggestion,
    showIcon,
    severity: determineSeverity(error),
  };
};

export default {
  extractErrorMessage,
  hasError,
  createError,
  formatErrorForDisplay,
  ErrorSeverity,
};
