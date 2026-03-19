/**
 * DB Explorer Commands - CQRS Pattern
 * React Query mutations for DB Explorer operations
 */
import { useMutation, useQueryClient } from '@tanstack/react-query';
import axiosInstance from '../axios';
import { dbExplorerKeys } from './queries';

/**
 * useAnalyzeMutation - Trigger database analysis
 */
export const useAnalyzeMutation = (options = {}) => {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async (connectionId) => {
            const response = await axiosInstance.post(`/api/db-explorer/${connectionId}/analyze`);
            return response.data;
        },
        onSuccess: (data, connectionId) => {
            // Invalidate all queries for this connection
            queryClient.invalidateQueries({ queryKey: dbExplorerKeys.all });
        },
        ...options,
    });
};

/**
 * useInvalidateCacheMutation - Clear cache for a connection
 */
export const useInvalidateCacheMutation = (options = {}) => {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async (connectionId) => {
            const response = await axiosInstance.delete(`/api/db-explorer/${connectionId}/cache`);
            return response.data;
        },
        onSuccess: (data, connectionId) => {
            // Invalidate all queries for this connection
            queryClient.invalidateQueries({ queryKey: dbExplorerKeys.all });
        },
        ...options,
    });
};
