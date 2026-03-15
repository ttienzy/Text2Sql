/**
 * Conversation Commands - CQRS Pattern
 * React Query mutation hooks for conversation operations
 */
import { useMutation, useQueryClient } from '@tanstack/react-query';
import axiosInstance from '../axios';
import { API_ENDPOINTS } from '../../constants';
import { conversationKeys } from './queries';

/**
 * useCreateConversationMutation - Create a new conversation
 * @param {Object} options - Mutation options
 * @param {Function} options.onSuccess - Callback on success
 * @param {Function} options.onError - Callback on error
 */
export const useCreateConversationMutation = ({ onSuccess, onError } = {}) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ connectionId, title }) => {
      const response = await axiosInstance.post(API_ENDPOINTS.CONVERSATIONS, {
        connectionId,
        title: title || 'New Conversation',
      });
      return response.data;
    },
    onSuccess: (data, variables) => {
      // Invalidate all conversation lists
      queryClient.invalidateQueries({ queryKey: conversationKeys.lists() });
      
      // Call custom onSuccess callback
      if (onSuccess) {
        onSuccess(data, variables);
      }
    },
    onError: (error, variables) => {
      // Call custom onError callback
      if (onError) {
        onError(error, variables);
      }
    },
  });
};

/**
 * useUpdateConversationMutation - Update a conversation (title, archive, etc.)
 * @param {Object} options - Mutation options
 * @param {Function} options.onSuccess - Callback on success
 * @param {Function} options.onError - Callback on error
 */
export const useUpdateConversationMutation = ({ onSuccess, onError } = {}) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, title, isArchived }) => {
      const response = await axiosInstance.put(`${API_ENDPOINTS.CONVERSATIONS}/${id}`, {
        title,
        isArchived,
      });
      return response.data;
    },
    onSuccess: (data, variables) => {
      // Update the specific conversation in cache
      queryClient.setQueryData(conversationKeys.detail(variables.id), data);
      
      // Invalidate all conversation lists
      queryClient.invalidateQueries({ queryKey: conversationKeys.lists() });
      
      // Call custom onSuccess callback
      if (onSuccess) {
        onSuccess(data, variables);
      }
    },
    onError: (error, variables) => {
      // Call custom onError callback
      if (onError) {
        onError(error, variables);
      }
    },
  });
};

/**
 * useDeleteConversationMutation - Delete a conversation
 * @param {Object} options - Mutation options
 * @param {Function} options.onSuccess - Callback on success
 * @param {Function} options.onError - Callback on error
 */
export const useDeleteConversationMutation = ({ onSuccess, onError } = {}) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (id) => {
      await axiosInstance.delete(`${API_ENDPOINTS.CONVERSATIONS}/${id}`);
      return id;
    },
    onSuccess: (deletedId, variables) => {
      // Remove the conversation from cache
      queryClient.removeQueries({ queryKey: conversationKeys.detail(deletedId) });
      
      // Invalidate all conversation lists
      queryClient.invalidateQueries({ queryKey: conversationKeys.lists() });
      
      // Call custom onSuccess callback
      if (onSuccess) {
        onSuccess(deletedId, variables);
      }
    },
    onError: (error, variables) => {
      // Call custom onError callback
      if (onError) {
        onError(error, variables);
      }
    },
  });
};

/**
 * useSendMessageMutation - Send a message to a conversation
 * @param {Object} options - Mutation options
 * @param {Function} options.onSuccess - Callback on success
 * @param {Function} options.onError - Callback on error
 */
export const useSendMessageMutation = ({ onSuccess, onError } = {}) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ conversationId, content, role = 'user' }) => {
      const response = await axiosInstance.post(
        `${API_ENDPOINTS.CONVERSATIONS}/${conversationId}/messages`,
        {
          content,
          role,
        }
      );
      return response.data;
    },
    onSuccess: (data, variables) => {
      // Invalidate the conversation to refresh messages
      queryClient.invalidateQueries({ 
        queryKey: conversationKeys.detail(variables.conversationId) 
      });
      
      // Call custom onSuccess callback
      if (onSuccess) {
        onSuccess(data, variables);
      }
    },
    onError: (error, variables) => {
      // Call custom onError callback
      if (onError) {
        onError(error, variables);
      }
    },
  });
};

export default {
  useCreateConversationMutation,
  useUpdateConversationMutation,
  useDeleteConversationMutation,
  useSendMessageMutation,
};
