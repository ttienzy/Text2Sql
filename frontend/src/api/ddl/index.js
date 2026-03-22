import { useState } from 'react';
import axios from '../axios';
import { API_ENDPOINTS } from '../../constants/api';

/**
 * API client for DDL operations (CREATE INDEX, ALTER TABLE, CREATE VIEW/PROC)
 */

/**
 * Generate preview with impact analysis
 * @param {Object} request - DDL operation request
 * @param {string} request.question - User's natural language question
 * @param {string} request.connectionId - Database connection ID
 * @param {string} [request.conversationId] - Optional conversation ID
 * @returns {Promise<Object>} Preview with DDL script and impact analysis
 */
export const generateDDLPreview = async (request) => {
    const response = await axios.post(API_ENDPOINTS.DDL.PREVIEW, request);
    return response.data;
};

/**
 * Execute DDL operation after user confirmation
 * @param {Object} request - Execution request
 * @param {string} request.question - Original question
 * @param {string} request.connectionId - Database connection ID
 * @param {string} [request.conversationId] - Optional conversation ID
 * @param {boolean} request.confirmed - User confirmation (must be true)
 * @param {Object} request.preview - Preview object from generateDDLPreview
 * @returns {Promise<Object>} Execution result
 */
export const executeDDLOperation = async (request) => {
    const response = await axios.post(API_ENDPOINTS.DDL.EXECUTE, request);
    return response.data;
};

/**
 * React hook for DDL operations with preview + execute flow
 */
export const useDDLOperation = () => {
    const [preview, setPreview] = useState(null);
    const [result, setResult] = useState(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);

    const generatePreview = async (question, connectionId, conversationId) => {
        setLoading(true);
        setError(null);
        setPreview(null);

        try {
            const data = await generateDDLPreview({
                question,
                connectionId,
                conversationId
            });

            if (data.preview.validationError) {
                setError(data.preview.validationError);
                return null;
            }

            setPreview(data.preview);
            return data.preview;
        } catch (err) {
            const errorMessage = err.response?.data?.error || err.message || 'Failed to generate preview';
            setError(errorMessage);
            return null;
        } finally {
            setLoading(false);
        }
    };

    const execute = async (question, connectionId, conversationId, previewData) => {
        setLoading(true);
        setError(null);
        setResult(null);

        try {
            const data = await executeDDLOperation({
                question,
                connectionId,
                conversationId,
                confirmed: true,
                preview: previewData || preview
            });

            setResult(data.result);
            return data.result;
        } catch (err) {
            const errorMessage = err.response?.data?.error || err.message || 'Failed to execute operation';
            setError(errorMessage);
            return null;
        } finally {
            setLoading(false);
        }
    };

    const reset = () => {
        setPreview(null);
        setResult(null);
        setError(null);
        setLoading(false);
    };

    return {
        preview,
        result,
        loading,
        error,
        generatePreview,
        execute,
        reset
    };
};

// For non-React usage
export default {
    generateDDLPreview,
    executeDDLOperation
};
