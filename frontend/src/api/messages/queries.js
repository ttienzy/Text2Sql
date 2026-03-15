/**
 * Messages Queries - CQRS Pattern
 * React Query hooks for fetching messages and schema data
 */
import { useQuery } from '@tanstack/react-query';
import axiosInstance from '../axios';
import { API_ENDPOINTS } from '../../constants';

/**
 * Query keys for message-related queries
 */
export const messageKeys = {
  all: ['messages'],
  lists: () => [...messageKeys.all, 'list'],
  list: (conversationId) => [...messageKeys.lists(), conversationId],
  details: () => [...messageKeys.all, 'detail'],
  detail: (conversationId, messageId) => [...messageKeys.details(), conversationId, messageId],
};

/**
 * Schema query keys
 */
export const schemaKeys = {
  all: ['schema'],
  byConnection: (connectionId) => [...schemaKeys.all, 'connection', connectionId],
};

/**
 * useMessagesQuery - Get messages for a conversation
 * @param {string} conversationId - The conversation ID
 * @param {Object} options - Query options
 * @param {number} options.limit - Max number of messages to return
 * @param {number} options.offset - Offset for pagination
 * @param {boolean} options.enabled - Whether to enable the query
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useMessagesQuery = (
  conversationId,
  { limit = 50, offset = 0, enabled = true, ...queryOptions } = {}
) => {
  return useQuery({
    queryKey: messageKeys.list(conversationId),
    queryFn: async () => {
      const response = await axiosInstance.get(
        `${API_ENDPOINTS.MESSAGES}/conversation/${conversationId}`,
        { params: { limit, offset } }
      );
      return response.data;
    },
    enabled: !!conversationId && enabled,
    staleTime: 1 * 60 * 1000, // 1 minute for messages
    ...queryOptions,
  });
};

/**
 * useSchemaQuery - Get schema for a connection (tables, columns, relationships)
 * @param {string} connectionId - The connection ID
 * @param {Object} options - Query options
 * @param {boolean} options.enabled - Whether to enable the query
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useSchemaQuery = (
  connectionId,
  { enabled = true, ...queryOptions } = {}
) => {
  return useQuery({
    queryKey: schemaKeys.byConnection(connectionId),
    queryFn: async () => {
      const response = await axiosInstance.get(
        `${API_ENDPOINTS.CONNECTIONS}/${connectionId}/schema`
      );
      return response.data;
    },
    enabled: !!connectionId && enabled,
    staleTime: 60 * 60 * 1000, // 1 hour for schema - it doesn't change often
    ...queryOptions,
  });
};

/**
 * useQuotaQuery - Get token quota information for current user
 * @param {Object} options - Query options
 * @param {boolean} options.enabled - Whether to enable the query
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useQuotaQuery = (
  { enabled = true, ...queryOptions } = {}
) => {
  return useQuery({
    queryKey: ['quota'],
    queryFn: async () => {
      const response = await axiosInstance.get('/api/auth/quota');
      return response.data;
    },
    enabled,
    staleTime: 30 * 1000, // 30 seconds
    refetchInterval: 30000, // Refetch every 30 seconds
    ...queryOptions,
  });
};

export default {
  useMessagesQuery,
  useSchemaQuery,
  useQuotaQuery,
};
