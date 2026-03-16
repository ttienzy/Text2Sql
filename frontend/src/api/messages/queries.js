/**
 * Message Queries - React Query hooks for fetching message data
 */
import { useQuery } from '@tanstack/react-query';
import axiosInstance from '../axios';

/**
 * Query keys for message-related queries
 */
export const messageKeys = {
  all: ['messages'],
  lists: () => [...messageKeys.all, 'list'],
  list: (filters) => [...messageKeys.lists(), filters],
  details: () => [...messageKeys.all, 'detail'],
  detail: (id) => [...messageKeys.details(), id],
  recentQueries: () => [...messageKeys.all, 'recent-queries'],
};

/**
 * useMessagesQuery - Get messages for a conversation
 */
export const useMessagesQuery = (conversationId, options = {}) => {
  return useQuery({
    queryKey: messageKeys.list({ conversationId }),
    queryFn: async () => {
      const response = await axiosInstance.get(`/api/messages/conversation/${conversationId}`);
      return response.data;
    },
    enabled: !!conversationId,
    ...options,
  });
};

/**
 * useRecentQueriesQuery - Get recent SQL queries for the current user
 */
export const useRecentQueriesQuery = (options = {}) => {
  return useQuery({
    queryKey: messageKeys.recentQueries(),
    queryFn: async () => {
      const response = await axiosInstance.get('/api/messages/recent-queries?limit=5');
      return response.data;
    },
    refetchInterval: 30000, // Refresh every 30 seconds
    ...options,
  });
};

export default {
  useMessagesQuery,
  useRecentQueriesQuery,
};