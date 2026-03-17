import { useMutation, useQuery } from '@tanstack/react-query';
import axios from '../axios';

/**
 * Process a message using the Conversation-Aware Agent (v2 API)
 * @param {Object} data - Request data
 * @param {string} data.connectionId - Database connection ID
 * @param {string} data.question - User question
 * @param {string} [data.conversationId] - Optional conversation ID
 * @param {boolean} [data.includeFullHistory] - Include full conversation history
 * @param {number} [data.maxHistoryMessages] - Maximum history messages to include
 * @returns {Promise} API response with enhanced conversation context
 */
export const processMessageV2 = async (data) => {
    const response = await axios.post('/api/v2/agent/process', {
        connectionId: data.connectionId,
        question: data.question,
        conversationId: data.conversationId,
        includeFullHistory: data.includeFullHistory ?? true,
        maxHistoryMessages: data.maxHistoryMessages ?? 20,
    });
    return response.data;
};

/**
 * Get conversation context and analytics
 * @param {string} conversationId - Conversation ID
 * @returns {Promise} Conversation context data
 */
export const getConversationContext = async (conversationId) => {
    const response = await axios.get(`/api/v2/agent/conversation/${conversationId}/context`);
    return response.data;
};

/**
 * React Query mutation hook for processing messages with conversation awareness
 * @param {Object} options - Mutation options
 * @returns {Object} Mutation object with enhanced response
 */
export const useProcessMessageV2Mutation = (options = {}) => {
    return useMutation({
        mutationFn: processMessageV2,
        ...options,
    });
};

/**
 * React Query hook for conversation context
 * @param {string} conversationId - Conversation ID
 * @param {Object} options - Query options
 * @returns {Object} Query object
 */
export const useConversationContextQuery = (conversationId, options = {}) => {
    return useQuery({
        queryKey: ['conversationContext', conversationId],
        queryFn: () => getConversationContext(conversationId),
        enabled: !!conversationId,
        ...options,
    });
};