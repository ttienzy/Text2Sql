/**
 * DB Explorer Commands - CQRS Pattern
 * React Query mutations for DB Explorer operations
 */
import { useMutation, useQueryClient } from '@tanstack/react-query';
import axiosInstance from '../axios';
import { dbExplorerKeys } from './queries';

/**
 * useAnalyzeMutation - Trigger database analysis
 * @param {Object} options - Mutation options
 * @param {string} options.mode - Analysis mode: 'overview' (default, fast) or 'full' (comprehensive)
 */
export const useAnalyzeMutation = (options = {}) => {
    const queryClient = useQueryClient();
    const { mode = 'overview', ...mutationOptions } = options;

    return useMutation({
        mutationFn: async (connectionId) => {
            const response = await axiosInstance.post(
                `/api/db-explorer/${connectionId}/analyze?mode=${mode}`
            );
            return response.data;
        },
        onSuccess: (data, connectionId) => {
            // Invalidate all queries for this connection
            queryClient.invalidateQueries({ queryKey: dbExplorerKeys.all });
        },
        ...mutationOptions,
    });
};

/**
 * useAnalyzeTableDetailMutation - Analyze single table in detail (on-demand)
 */
export const useAnalyzeTableDetailMutation = (options = {}) => {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async ({ connectionId, tableName }) => {
            const response = await axiosInstance.post(
                `/api/db-explorer/${connectionId}/tables/${tableName}/analyze`
            );
            return response.data;
        },
        onSuccess: (data, { connectionId, tableName }) => {
            // Invalidate table detail query
            queryClient.invalidateQueries({
                queryKey: dbExplorerKeys.tableDetail(connectionId, tableName),
            });
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

/**
 * useExportDocumentationMutation - Export database documentation
 */
export const useExportDocumentationMutation = (options = {}) => {
    return useMutation({
        mutationFn: async ({ connectionId, format }) => {
            const response = await axiosInstance.get(
                `/api/db-explorer/${connectionId}/export?format=${format}`,
                { responseType: 'blob' }
            );
            return { data: response.data, format };
        },
        ...options,
    });
};

/**
 * useSaveSemanticProfileMutation - Save Redis-backed semantic profile
 */
export const useSaveSemanticProfileMutation = (options = {}) => {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async ({ connectionId, profile }) => {
            const response = await axiosInstance.put(
                `/api/db-explorer/${connectionId}/semantic-profile`,
                profile
            );
            return response.data;
        },
        onSuccess: (data, { connectionId }) => {
            queryClient.invalidateQueries({ queryKey: dbExplorerKeys.semanticProfile(connectionId) });
            queryClient.invalidateQueries({ queryKey: dbExplorerKeys.tables(connectionId) });
            queryClient.invalidateQueries({ queryKey: dbExplorerKeys.all });
        },
        ...options,
    });
};

/**
 * useDeleteSemanticProfileMutation - Delete Redis-backed semantic profile
 */
export const useDeleteSemanticProfileMutation = (options = {}) => {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async (connectionId) => {
            const response = await axiosInstance.delete(
                `/api/db-explorer/${connectionId}/semantic-profile`
            );
            return response.data;
        },
        onSuccess: (data, connectionId) => {
            queryClient.invalidateQueries({ queryKey: dbExplorerKeys.semanticProfile(connectionId) });
            queryClient.invalidateQueries({ queryKey: dbExplorerKeys.tables(connectionId) });
            queryClient.invalidateQueries({ queryKey: dbExplorerKeys.all });
        },
        ...options,
    });
};
