import { useMutation } from '@tanstack/react-query';
import axios from '../axios';

/**
 * Process a message using the Enhanced Agent Orchestrator
 * @param {Object} data - Request data
 * @param {string} data.connectionId - Database connection ID
 * @param {string} data.question - User question
 * @param {string} [data.conversationId] - Optional conversation ID
 * @returns {Promise} API response
 */
export const processMessage = async (data) => {
    const response = await axios.post('/api/agent/process', data);
    return response.data;
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