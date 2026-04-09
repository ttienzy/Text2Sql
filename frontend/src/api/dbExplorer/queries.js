/**
 * DB Explorer Queries - CQRS Pattern
 * React Query hooks for fetching database explorer data
 */
import { useQuery } from '@tanstack/react-query';
import axiosInstance from '../axios';

/**
 * Query keys for DB Explorer
 */
export const dbExplorerKeys = {
    all: ['dbExplorer'],
    status: (connectionId) => [...dbExplorerKeys.all, 'status', connectionId],
    overview: (connectionId) => [...dbExplorerKeys.all, 'overview', connectionId],
    tables: (connectionId, filters) => [...dbExplorerKeys.all, 'tables', connectionId, filters],
    tableDetail: (connectionId, tableName) => [...dbExplorerKeys.all, 'table', connectionId, tableName],
    health: (connectionId) => [...dbExplorerKeys.all, 'health', connectionId],
    graph: (connectionId) => [...dbExplorerKeys.all, 'graph', connectionId],
};

/**
 * useConnectionInfoQuery - Get connection details
 */
export const useConnectionInfoQuery = (connectionId, options = {}) => {
    return useQuery({
        queryKey: ['connection', connectionId],
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/connections/${connectionId}`);
            return response.data;
        },
        enabled: !!connectionId,
        staleTime: 1000 * 60 * 5, // 5 minutes
        ...options,
    });
};

/**
 * useStatusQuery - Get cache status
 */
export const useStatusQuery = (connectionId, options = {}) => {
    return useQuery({
        queryKey: dbExplorerKeys.status(connectionId),
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/status`);
            return response.data;
        },
        enabled: !!connectionId,
        staleTime: 1000 * 10, // 10 seconds
        ...options,
    });
};

/**
 * useOverviewQuery - Get database overview
 */
export const useOverviewQuery = (connectionId, options = {}) => {
    return useQuery({
        queryKey: dbExplorerKeys.overview(connectionId),
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/overview`);
            return response.data;
        },
        enabled: !!connectionId,
        staleTime: 1000 * 60 * 60, // 1 hour
        ...options,
    });
};

/**
 * useTablesQuery - Get table list with filters
 */
export const useTablesQuery = (connectionId, filters = {}, options = {}) => {
    return useQuery({
        queryKey: dbExplorerKeys.tables(connectionId, filters),
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/tables`, {
                params: filters,
            });
            return response.data;
        },
        enabled: !!connectionId,
        staleTime: 1000 * 60 * 60, // 1 hour
        ...options,
    });
};

/**
 * useTableDetailQuery - Get table detail
 */
export const useTableDetailQuery = (connectionId, tableName, options = {}) => {
    return useQuery({
        queryKey: dbExplorerKeys.tableDetail(connectionId, tableName),
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/tables/${tableName}`);
            return response.data;
        },
        enabled: !!connectionId && !!tableName,
        staleTime: 1000 * 60 * 60, // 1 hour
        ...options,
    });
};

/**
 * useHealthQuery - Get health check report
 */
export const useHealthQuery = (connectionId, options = {}) => {
    return useQuery({
        queryKey: dbExplorerKeys.health(connectionId),
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/health`);
            return response.data;
        },
        enabled: !!connectionId,
        staleTime: 1000 * 60 * 60 * 24, // 24 hours
        ...options,
    });
};

/**
 * useGraphQuery - Get ER graph data
 */
export const useGraphQuery = (connectionId, options = {}) => {
    return useQuery({
        queryKey: dbExplorerKeys.graph(connectionId),
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/graph`);
            return response.data;
        },
        enabled: !!connectionId,
        staleTime: 1000 * 60 * 60, // 1 hour
        ...options,
    });
};

/**
 * useSampleDataQuery - Get sample data from a table (top 5 rows)
 */
export const useSampleDataQuery = (connectionId, tableName, options = {}) => {
    return useQuery({
        queryKey: [...dbExplorerKeys.all, 'sampleData', connectionId, tableName],
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/tables/${tableName}/sample`);
            return response.data;
        },
        enabled: !!connectionId && !!tableName && options.enabled !== false,
        staleTime: 1000 * 60 * 5, // 5 minutes
        ...options,
    });
};

/**
 * useQuerySuggestionsQuery - Get smart query suggestions for a table
 */
export const useQuerySuggestionsQuery = (connectionId, tableName, options = {}) => {
    return useQuery({
        queryKey: [...dbExplorerKeys.all, 'suggestions', connectionId, tableName],
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/tables/${tableName}/suggestions`);
            return response.data;
        },
        enabled: !!connectionId && !!tableName && options.enabled !== false,
        staleTime: 1000 * 60 * 30, // 30 minutes
        ...options,
    });
};

/**
 * useSchemaChangesQuery - Get schema changes compared to cached version
 */
export const useSchemaChangesQuery = (connectionId, options = {}) => {
    return useQuery({
        queryKey: [...dbExplorerKeys.all, 'changes', connectionId],
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/changes`);
            return response.data;
        },
        enabled: !!connectionId && options.enabled !== false,
        staleTime: 1000 * 60, // 1 minute
        ...options,
    });
};

/**
 * useSemanticSearchQuery - Search tables using semantic search (Qdrant)
 */
export const useSemanticSearchQuery = (connectionId, query, options = {}) => {
    return useQuery({
        queryKey: [...dbExplorerKeys.all, 'search', connectionId, query],
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/search`, {
                params: {
                    query,
                    limit: options.limit || 10,
                    scoreThreshold: options.scoreThreshold || 0.7,
                },
            });
            return response.data;
        },
        enabled: !!connectionId && !!query && query.length >= 2,
        staleTime: 1000 * 60 * 5, // 5 minutes
        ...options,
    });
};

/**
 * useIndexRecommendationsQuery - Get index recommendations for database
 */
export const useIndexRecommendationsQuery = (connectionId, options = {}) => {
    return useQuery({
        queryKey: [...dbExplorerKeys.all, 'indexRecommendations', connectionId],
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/index-recommendations`);
            return response.data;
        },
        enabled: !!connectionId && options.enabled !== false,
        staleTime: 1000 * 60 * 60, // 1 hour
        ...options,
    });
};

/**
 * useNamingAnalysisQuery - Get naming convention analysis for database
 */
export const useNamingAnalysisQuery = (connectionId, options = {}) => {
    return useQuery({
        queryKey: [...dbExplorerKeys.all, 'namingAnalysis', connectionId],
        queryFn: async () => {
            const response = await axiosInstance.get(`/api/db-explorer/${connectionId}/naming-analysis`);
            return response.data;
        },
        enabled: !!connectionId && options.enabled !== false,
        staleTime: 1000 * 60 * 60, // 1 hour
        ...options,
    });
};
