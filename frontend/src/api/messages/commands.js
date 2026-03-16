/**
 * Messages Commands - CQRS Pattern
 * React Query mutation hooks for message operations with optimistic updates
 */
import { useMutation, useQueryClient } from '@tanstack/react-query';
import axiosInstance from '../axios';
import { messageKeys } from './queries';

/**
 * useSendMessageMutation - Send a question to the agent and get SQL + results
 * Supports optimistic updates for better UX
 * @param {Object} options - Mutation options
 * @param {Function} options.onSuccess - Callback on success
 * @param {Function} options.onError - Callback on error
 * @param {Function} options.onMutate - Callback when mutation starts (for optimistic update)
 */
export const useSendMessageMutation = ({ onSuccess, onError, onMutate } = {}) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ conversationId, question }) => {
      const response = await axiosInstance.post(
        `/api/messages/conversation/${conversationId}`,
        { question }
      );
      return response.data;
    },

    // Called before the mutation function
    onMutate: async (variables) => {
      const { conversationId, question } = variables;

      // Cancel any outgoing refetches to avoid overwriting our optimistic update
      await queryClient.cancelQueries({ queryKey: messageKeys.list(conversationId) });

      // Snapshot the previous messages
      const previousMessages = queryClient.getQueryData(messageKeys.list(conversationId));

      // Optimistically add the user message
      const optimisticUserMessage = {
        id: `temp-${Date.now()}`,
        conversationId,
        role: 'user',
        content: question,
        createdAt: new Date().toISOString(),
        isOptimistic: true,
      };

      // Add pending assistant message
      const optimisticAssistantMessage = {
        id: `temp-${Date.now()}-response`,
        conversationId,
        role: 'assistant',
        content: '',
        isPending: true,
        createdAt: new Date().toISOString(),
      };

      // Optimistically update the messages cache
      queryClient.setQueryData(messageKeys.list(conversationId), (old = []) => [
        ...old,
        optimisticUserMessage,
        optimisticAssistantMessage,
      ]);

      // Call custom onMutate callback
      if (onMutate) {
        onMutate(variables);
      }

      // Return context with previous messages for rollback
      return { previousMessages, optimisticUserMessage, optimisticAssistantMessage };
    },

    // Called on error
    onError: (error, variables, context) => {
      const { conversationId } = variables;
      const { previousMessages } = context || {};

      // Rollback to previous messages
      if (previousMessages) {
        queryClient.setQueryData(messageKeys.list(conversationId), previousMessages);
      } else {
        // If no previous data, invalidate the query
        queryClient.invalidateQueries({ queryKey: messageKeys.list(conversationId) });
      }

      // Remove optimistic messages on error
      queryClient.setQueryData(messageKeys.list(conversationId), (old = []) =>
        old.filter(m => !m.isOptimistic && !m.isPending)
      );

      // Call custom onError callback
      if (onError) {
        onError(error, variables);
      }
    },

    // Called on success
    onSuccess: (data, variables, context) => {
      const { conversationId } = variables;
      const { previousMessages } = context || {};

      // Replace optimistic messages with actual response
      const responseMessage = {
        ...data,
        id: data.id || `response-${Date.now()}`,
        role: 'assistant',
      };

      // Get the user message from the previous state or context
      let finalMessages = [];

      if (previousMessages && Array.isArray(previousMessages)) {
        finalMessages = [...previousMessages, responseMessage];
      } else {
        // Get current messages and remove optimistic ones
        const currentMessages = queryClient.getQueryData(messageKeys.list(conversationId)) || [];
        finalMessages = [
          ...currentMessages.filter(m => !m.isOptimistic && !m.isPending),
          responseMessage,
        ];
      }

      queryClient.setQueryData(messageKeys.list(conversationId), finalMessages);

      // Call custom onSuccess callback
      if (onSuccess) {
        onSuccess(data, variables);
      }
    },

    // Always refetch after error or success (optional - can be disabled for performance)
    onSettled: (data, error, variables) => {
      const { conversationId } = variables;

      // Invalidate to ensure we're in sync with server
      queryClient.invalidateQueries({ queryKey: messageKeys.list(conversationId) });
    },
  });
};

/**
 * useAgentQueryMutation - Send a query directly to the AI agent
 * This is an alternative endpoint for more advanced agent interactions
 * @param {Object} options - Mutation options
 * @param {Function} options.onSuccess - Callback on success
 * @param {Function} options.onError - Callback on error
 */
export const useAgentQueryMutation = ({ onSuccess, onError } = {}) => {
  return useMutation({
    mutationFn: async ({ question, connectionId, context }) => {
      const response = await axiosInstance.post('/api/agent/query', {
        question,
        connectionId,
        context,
      });
      return response.data;
    },
    onSuccess: (data, variables) => {
      if (onSuccess) {
        onSuccess(data, variables);
      }
    },
    onError: (error, variables) => {
      if (onError) {
        onError(error, variables);
      }
    },
  });
};

export default {
  useSendMessageMutation,
  useAgentQueryMutation,
};
