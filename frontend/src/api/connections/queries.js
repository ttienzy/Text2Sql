/**
 * Connection Queries - CQRS Pattern
 * React Query hooks for fetching connection data
 */
import { useQuery } from '@tanstack/react-query';
import axiosInstance from '../axios';
import { API_ENDPOINTS } from '../../constants';

/**
 * Query keys for connection-related queries
 */
export const connectionKeys = {
  all: ['connections'],
  lists: () => [...connectionKeys.all, 'list'],
  list: (filters) => [...connectionKeys.lists(), filters],
  details: () => [...connectionKeys.all, 'detail'],
  detail: (id) => [...connectionKeys.details(), id],
};

/**
 * useConnectionsQuery - Get all connections for the current user
 * @param {Object} options - Query options
 * @param {boolean} options.includeDeleted - Include deleted connections
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useConnectionsQuery = ({ includeDeleted = false, ...queryOptions } = {}) => {
  return useQuery({
    queryKey: connectionKeys.list({ includeDeleted }),
    queryFn: async () => {
      const response = await axiosInstance.get(API_ENDPOINTS.CONNECTIONS, {
        params: { includeDeleted },
      });
      return response.data;
    },
    ...queryOptions,
  });
};

/**
 * useConnectionQuery - Get a single connection by ID
 * @param {string} connectionId - The connection ID
 * @param {Object} options - Query options
 * @param {boolean} options.enabled - Whether to enable the query
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useConnectionQuery = (connectionId, { enabled = true, ...queryOptions } = {}) => {
  return useQuery({
    queryKey: connectionKeys.detail(connectionId),
    queryFn: async () => {
      const response = await axiosInstance.get(`${API_ENDPOINTS.CONNECTIONS}/${connectionId}`);
      return response.data;
    },
    enabled: !!connectionId && enabled,
    ...queryOptions,
  });
};

export default {
  useConnectionsQuery,
  useConnectionQuery,
};
