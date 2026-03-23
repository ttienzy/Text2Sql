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
 * @returns {Promise<UnifiedPipelineResponse>} Unified response with preview
 */
export const generateWritePreview = async (request) => {
    const response = await axios.post(API_ENDPOINTS.WRITE.PREVIEW, request);
    return response.data; // UnifiedPipelineResponse<WritePipelineData>
};

/**
 * Execute write operation after user confirmation
 * @param {Object} request - Execution request
 * @param {string} request.question - Original question
 * @param {string} request.connectionId - Database connection ID
 * @param {string} [request.conversationId] - Optional conversation ID
 * @param {boolean} request.confirmed - User confirmation (must be true)
 * @param {Object} request.preview - Preview object from generateWritePreview
 * @returns {Promise<UnifiedPipelineResponse>} Unified response with result
 */
export const executeWriteOperation = async (request) => {
    const response = await axios.post(API_ENDPOINTS.WRITE.EXECUTE, request);
    return response.data; // UnifiedPipelineResponse<WritePipelineData>
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
            const response = await generateWritePreview({
                question,
                connectionId,
                conversationId
            });

            // Extract preview from UnifiedPipelineResponse
            // Note: response.data = UnifiedPipelineResponse, Data property contains WritePipelineData
            const previewData = response.data?.Data?.preview;

            if (!previewData || response.error) {
                const errorMsg = response.error?.message || response.message || 'Failed to generate preview';
                setError(errorMsg);
                return null;
            }

            setPreview(previewData);
            return previewData;
        } catch (err) {
            const errorMessage = err.response?.data?.error?.message
                || err.response?.data?.message
                || err.message
                || 'Failed to generate preview';
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
            const response = await executeWriteOperation({
                question,
                connectionId,
                conversationId,
                confirmed: true,
                preview: previewData || preview
            });

            // Extract result from UnifiedPipelineResponse
            const resultData = response.data?.Data?.result;

            if (!resultData || response.error) {
                const errorMsg = response.error?.message || response.message || 'Failed to execute operation';
                setError(errorMsg);
                return null;
            }

            setResult(resultData);
            return resultData;
        } catch (err) {
            const errorMessage = err.response?.data?.error?.message
                || err.response?.data?.message
                || err.message
                || 'Failed to execute operation';
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
