/**
 * Connection Commands - CQRS Pattern
 * React Query mutation hooks for connection operations
 */
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import axiosInstance from '../axios';
import { API_ENDPOINTS } from '../../constants';
import { connectionKeys } from './queries';

/**
 * useCreateConnectionMutation - Create a new database connection
 * @param {Object} options - Mutation options
 */
export const useCreateConnectionMutation = (options = {}) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (connectionData) => {
      const response = await axiosInstance.post(API_ENDPOINTS.CONNECTIONS, connectionData);
      return response.data;
    },
    onMutate: async (newConnection) => {
      // Cancel any outgoing refetches
      await queryClient.cancelQueries({ queryKey: connectionKeys.lists() });

      // Snapshot the previous value
      const previousConnections = queryClient.getQueryData(connectionKeys.lists());

      // Optimistically update
      queryClient.setQueryData(connectionKeys.lists(), (old = []) => [
        ...old,
        { ...newConnection, id: 'temp-' + Date.now() },
      ]);

      return { previousConnections };
    },
    onError: (err, newConnection, context) => {
      // Rollback on error
      if (context?.previousConnections) {
        queryClient.setQueryData(connectionKeys.lists(), context.previousConnections);
      }
      message.error(err.response?.data?.message || 'Failed to create connection');
    },
    onSuccess: (data) => {
      // Update the cache with the real data
      queryClient.setQueryData(connectionKeys.lists(), (old = []) => 
        old.map(c => c.id?.startsWith('temp-') ? data : c)
      );
      message.success('Connection created successfully');
    },
    onSettled: () => {
      // Invalidate the connections list
      queryClient.invalidateQueries({ queryKey: connectionKeys.lists() });
    },
    ...options,
  });
};

/**
 * useUpdateConnectionMutation - Update an existing connection
 * @param {Object} options - Mutation options
 */
export const useUpdateConnectionMutation = (options = {}) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, data }) => {
      const response = await axiosInstance.put(`${API_ENDPOINTS.CONNECTIONS}/${id}`, data);
      return response.data;
    },
    onMutate: async ({ id, data }) => {
      // Cancel any outgoing refetches
      await queryClient.cancelQueries({ queryKey: connectionKeys.lists() });
      await queryClient.cancelQueries({ queryKey: connectionKeys.detail(id) });

      // Snapshot the previous values
      const previousConnections = queryClient.getQueryData(connectionKeys.lists());
      const previousConnection = queryClient.getQueryData(connectionKeys.detail(id));

      // Optimistically update
      queryClient.setQueryData(connectionKeys.lists(), (old = []) =>
        old.map(c => (c.id === id ? { ...c, ...data } : c))
      );
      queryClient.setQueryData(connectionKeys.detail(id), (old) => ({
        ...old,
        ...data,
      }));

      return { previousConnections, previousConnection };
    },
    onError: (err, variables, context) => {
      // Rollback on error
      if (context?.previousConnections) {
        queryClient.setQueryData(connectionKeys.lists(), context.previousConnections);
      }
      if (context?.previousConnection) {
        queryClient.setQueryData(connectionKeys.detail(variables.id), context.previousConnection);
      }
      message.error(err.response?.data?.message || 'Failed to update connection');
    },
    onSuccess: () => {
      message.success('Connection updated successfully');
    },
    onSettled: (_, variables) => {
      // Invalidate queries
      queryClient.invalidateQueries({ queryKey: connectionKeys.lists() });
      queryClient.invalidateQueries({ queryKey: connectionKeys.detail(variables.id) });
    },
    ...options,
  });
};

/**
 * useDeleteConnectionMutation - Soft delete a connection
 * @param {Object} options - Mutation options
 */
export const useDeleteConnectionMutation = (options = {}) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (connectionId) => {
      await axiosInstance.delete(`${API_ENDPOINTS.CONNECTIONS}/${connectionId}`);
      return connectionId;
    },
    onMutate: async (connectionId) => {
      // Cancel any outgoing refetches
      await queryClient.cancelQueries({ queryKey: connectionKeys.lists() });

      // Snapshot the previous value
      const previousConnections = queryClient.getQueryData(connectionKeys.lists());

      // Optimistically remove
      queryClient.setQueryData(connectionKeys.lists(), (old = []) =>
        old.filter(c => c.id !== connectionId)
      );

      return { previousConnections };
    },
    onError: (err, connectionId, context) => {
      // Rollback on error
      if (context?.previousConnections) {
        queryClient.setQueryData(connectionKeys.lists(), context.previousConnections);
      }
      message.error(err.response?.data?.message || 'Failed to delete connection');
    },
    onSuccess: () => {
      message.success('Connection deleted successfully');
    },
    onSettled: () => {
      // Invalidate the connections list
      queryClient.invalidateQueries({ queryKey: connectionKeys.lists() });
    },
    ...options,
  });
};

/**
 * useTestConnectionMutation - Test a database connection
 * @param {Object} options - Mutation options
 */
export const useTestConnectionMutation = (options = {}) => {
  return useMutation({
    mutationFn: async (connectionData) => {
      // If connectionData has an ID, test existing connection
      // Otherwise, test the provided data (for new connections)
      const url = connectionData.id
        ? `${API_ENDPOINTS.CONNECTIONS}/${connectionData.id}/test`
        : `${API_ENDPOINTS.CONNECTIONS}/test`;
      
      const response = await axiosInstance.post(url, connectionData);
      return response.data;
    },
    onError: (err) => {
      message.error(err.response?.data?.message || 'Connection test failed');
    },
    ...options,
  });
};

/**
 * useSyncSchemaMutation - Synchronize schema from database
 * @param {Object} options - Mutation options
 */
export const useSyncSchemaMutation = (options = {}) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (connectionId) => {
      const response = await axiosInstance.post(`${API_ENDPOINTS.CONNECTIONS}/${connectionId}/sync`);
      return response.data;
    },
    onMutate: async (connectionId) => {
      // Cancel any outgoing refetches
      await queryClient.cancelQueries({ queryKey: connectionKeys.detail(connectionId) });

      // Snapshot the previous value
      const previousConnection = queryClient.getQueryData(connectionKeys.detail(connectionId));

      // Optimistically update sync status
      queryClient.setQueryData(connectionKeys.detail(connectionId), (old) => ({
        ...old,
        schemaSync: {
          isSynced: false,
          tableCount: 0,
          columnCount: 0,
        },
      }));

      return { previousConnection };
    },
    onError: (err, connectionId, context) => {
      // Rollback on error
      if (context?.previousConnection) {
        queryClient.setQueryData(connectionKeys.detail(connectionId), context.previousConnection);
      }
      message.error(err.response?.data?.message || 'Schema sync failed');
    },
    onSuccess: () => {
      message.success('Schema synchronized successfully');
    },
    onSettled: (_, connectionId) => {
      // Invalidate connection detail
      queryClient.invalidateQueries({ queryKey: connectionKeys.detail(connectionId) });
    },
    ...options,
  });
};

export default {
  useCreateConnectionMutation,
  useUpdateConnectionMutation,
  useDeleteConnectionMutation,
  useTestConnectionMutation,
  useSyncSchemaMutation,
};
