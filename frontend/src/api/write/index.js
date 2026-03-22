import { useState } from 'react';
import axios from '../axios';
import { API_ENDPOINTS } from '../../constants/api';

/**
 * API client for WRITE operations (INSERT/UPDATE)
 */

/**
 * Generate preview of write operation
 * @param {Object} request - Write operation request
 * @param {string} request.question - User's natural language question
 * @param {string} request.connectionId - Database connection ID
 * @param {string} [request.conversationId] - Optional conversation ID
 * @returns {Promise<Object>} Preview with SQL, estimated rows, warnings
 */
export const generateWritePreview = async (request) => {
    const response = await axios.post(API_ENDPOINTS.WRITE.PREVIEW, request);
    return response.data;
};

/**
 * Execute write operation after user confirmation
 * @param {Object} request - Execution request
 * @param {string} request.question - Original question
 * @param {string} request.connectionId - Database connection ID
 * @param {string} [request.conversationId] - Optional conversation ID
 * @param {boolean} request.confirmed - User confirmation (must be true)
 * @param {Object} request.preview - Preview object from generateWritePreview
 * @returns {Promise<Object>} Execution result with affected rows
 */
export const executeWriteOperation = async (request) => {
    const response = await axios.post(API_ENDPOINTS.WRITE.EXECUTE, request);
    return response.data;
};

/**
 * React hook for write operations with preview + execute flow
 */
export const useWriteOperation = () => {
    const [preview, setPreview] = useState(null);
    const [result, setResult] = useState(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);

    const generatePreview = async (question, connectionId, conversationId) => {
        setLoading(true);
        setError(null);
        setPreview(null);

        try {
            const data = await generateWritePreview({
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
            const data = await executeWriteOperation({
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
    generateWritePreview,
    executeWriteOperation
};
