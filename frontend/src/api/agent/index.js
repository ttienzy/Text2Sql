import { useMutation } from '@tanstack/react-query';
import axios from '../axios';

/**
 * Process a message using the Enhanced Agent Orchestrator
 * NOW RETURNS: UnifiedPipelineResponse
 * 
 * @param {Object} data - Request data
 * @param {string} data.connectionId - Database connection ID
 * @param {string} data.question - User question
 * @param {string} [data.conversationId] - Optional conversation ID
 * @returns {Promise<UnifiedPipelineResponse>} Unified pipeline response
 */
export const processMessage = async (data) => {
    const response = await axios.post('/api/agent/process', data);
    return response.data; // UnifiedPipelineResponse
};

/**
 * React Query mutation hook for processing messages
 * @param {Object} options - Mutation options
 * @returns {Object} Mutation object
 */
export const useProcessMessageMutation = (options = {}) => {
    return useMutation({
        mutationFn: processMessage,
        ...options,
    });
};

/**
 * Confirm a pending DML/DDL execution
 * @param {string} confirmId - The ID of the pending confirmation
 * @returns {Promise<any>}
 */
export const confirmExecution = async (confirmId) => {
    const response = await axios.post(`/api/agent/confirm/${confirmId}`);
    return response.data;
};

/**
 * Cancel a pending DML/DDL execution
 * @param {string} confirmId - The ID of the pending confirmation
 * @param {string} reason - Reason for cancellation
 * @returns {Promise<any>}
 */
export const cancelExecution = async (confirmId, reason = 'User cancelled') => {
    const response = await axios.post(`/api/agent/confirm/${confirmId}/cancel`, { reason });
    return response.data;
};