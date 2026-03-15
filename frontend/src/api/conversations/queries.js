/**
 * Conversation Queries - CQRS Pattern
 * React Query hooks for fetching conversation data
 */
import { useQuery } from '@tanstack/react-query';
import axiosInstance from '../axios';
import { API_ENDPOINTS } from '../../constants';

/**
 * Query keys for conversation-related queries
 */
export const conversationKeys = {
  all: ['conversations'],
  lists: () => [...conversationKeys.all, 'list'],
  list: (connectionId, filters) => [...conversationKeys.lists(), { connectionId, ...filters }],
  details: () => [...conversationKeys.all, 'detail'],
  detail: (id) => [...conversationKeys.details(), id],
  messages: (conversationId) => [...conversationKeys.all, 'messages', conversationId],
};

/**
 * useConversationsQuery - Get all conversations (optionally filtered by connection)
 * @param {string|null} connectionId - Filter by connection ID
 * @param {Object} options - Query options
 * @param {number} options.limit - Max number of conversations to return
 * @param {number} options.offset - Offset for pagination
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useConversationsQuery = (
  connectionId,
  { limit = 50, offset = 0, ...queryOptions } = {}
) => {
  return useQuery({
    queryKey: conversationKeys.list(connectionId, { limit, offset }),
    queryFn: async () => {
      const response = await axiosInstance.get(API_ENDPOINTS.CONVERSATIONS, {
        params: { connectionId, limit, offset },
      });
      
      // Filter by connectionId on client-side if backend doesn't support it
      let conversations = response.data;
      if (connectionId) {
        conversations = conversations.filter(c => c.connectionId === connectionId);
      }
      
      return conversations;
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
    ...queryOptions,
  });
};

/**
 * useConversationQuery - Get a single conversation by ID
 * @param {string} conversationId - The conversation ID
 * @param {Object} options - Query options
 * @param {boolean} options.enabled - Whether to enable the query
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useConversationQuery = (
  conversationId,
  { enabled = true, ...queryOptions } = {}
) => {
  return useQuery({
    queryKey: conversationKeys.detail(conversationId),
    queryFn: async () => {
      const response = await axiosInstance.get(`${API_ENDPOINTS.CONVERSATIONS}/${conversationId}`);
      return response.data;
    },
    enabled: !!conversationId && enabled,
    staleTime: 5 * 60 * 1000, // 5 minutes
    ...queryOptions,
  });
};

/**
 * useConversationMessagesQuery - Get messages for a conversation
 * @param {string} conversationId - The conversation ID
 * @param {Object} options - Query options
 * @param {boolean} options.enabled - Whether to enable the query
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useConversationMessagesQuery = (
  conversationId,
  { enabled = true, ...queryOptions } = {}
) => {
  return useQuery({
    queryKey: conversationKeys.messages(conversationId),
    queryFn: async () => {
      // The conversation detail already includes messages, so we can use it
      const response = await axiosInstance.get(`${API_ENDPOINTS.CONVERSATIONS}/${conversationId}`);
      return response.data.messages || [];
    },
    enabled: !!conversationId && enabled,
    staleTime: 1 * 60 * 1000, // 1 minute for messages
    ...queryOptions,
  });
};

export default {
  useConversationsQuery,
  useConversationQuery,
  useConversationMessagesQuery,
};
