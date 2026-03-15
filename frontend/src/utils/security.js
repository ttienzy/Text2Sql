/**
 * Security utilities for XSS prevention
 */

/**
 * Escapes HTML special characters to prevent XSS attacks
 * @param {string} text - The text to escape
 * @returns {string} - The escaped text
 */
export const escapeHtml = (text) => {
  if (text == null) return '';
  const str = String(text);
  const escapeMap = {
    '&': '&',
    '<': '<',
    '>': '>',
    '"': '"',
    "'": '&#x27;',
    '/': '&#x2F;',
    '`': '&#96;',
  };
  return str.replace(/[&<>"'`/]/g, (char) => escapeMap[char]);
};

/**
 * Sanitizes user input by escaping HTML characters
 * @param {string} input - User input to sanitize
 * @returns {string} - Sanitized input
 */
export const sanitizeInput = (input) => {
  if (input == null) return '';
  return escapeHtml(String(input).trim());
};

/**
 * Creates a safe HTML string by escaping user content before adding markup
 * This should be used instead of directly inserting user content into innerHTML
 * @param {string} userContent - User content to make safe
 * @param {string} wrapper - Optional HTML wrapper (e.g., '<span>{content}</span>')
 * @returns {string} - Safe HTML string
 */
export const createSafeHtml = (userContent, wrapper = null) => {
  const safeContent = escapeHtml(userContent);
  if (wrapper) {
    return wrapper.replace('{content}', safeContent);
  }
  return safeContent;
};

/**
 * Strips potentially dangerous tags from HTML
 * @param {string} html - HTML string to sanitize
 * @returns {string} - Sanitized HTML
 */
export const stripDangerousTags = (html) => {
  if (html == null) return '';
  const str = String(html);
  // Remove script tags
  return str.replace(/<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>/gi, '');
};
