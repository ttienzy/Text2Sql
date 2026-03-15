/**
 * Quota & Observability Queries - CQRS Pattern
 * React Query hooks for fetching quota and usage data
 */
import { useQuery } from '@tanstack/react-query';
import axiosInstance from '../axios';

/**
 * Query keys for quota-related queries
 */
export const quotaKeys = {
  all: ['quota'],
  quota: () => [...quotaKeys.all, 'current'],
  usageHistory: (filters) => [...quotaKeys.all, 'history', filters],
  usageByConversation: () => [...quotaKeys.all, 'by-conversation'],
  usageByModel: () => [...quotaKeys.all, 'by-model'],
};

/**
 * useQuotaQuery - Get current user's token quota information
 * @param {Object} options - Query options
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useQuotaQuery = ({ queryOptions } = {}) => {
  return useQuery({
    queryKey: quotaKeys.quota(),
    queryFn: async () => {
      const response = await axiosInstance.get('/api/auth/quota');
      return response.data;
    },
    staleTime: 1 * 60 * 1000, // 1 minute
    refetchOnWindowFocus: true,
    ...queryOptions,
  });
};

/**
 * useUsageHistoryQuery - Get token usage history
 * @param {Object} options - Query options
 * @param {string} options.from - Start date (ISO string)
 * @param {string} options.to - End date (ISO string)
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useUsageHistoryQuery = ({ from, to, queryOptions } = {}) => {
  return useQuery({
    queryKey: quotaKeys.usageHistory({ from, to }),
    queryFn: async () => {
      const response = await axiosInstance.get('/api/observability/usage-history', {
        params: { from, to },
      });
      return response.data;
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
    refetchOnWindowFocus: true,
    enabled: !!from && !!to,
    ...queryOptions,
  });
};

/**
 * useUsageByConversationQuery - Get token usage grouped by conversation
 * @param {Object} options - Query options
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useUsageByConversationQuery = ({ queryOptions } = {}) => {
  return useQuery({
    queryKey: quotaKeys.usageByConversation(),
    queryFn: async () => {
      const response = await axiosInstance.get('/api/observability/usage-by-conversation');
      return response.data;
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
    refetchOnWindowFocus: true,
    ...queryOptions,
  });
};

/**
 * useUsageByModelQuery - Get token usage grouped by AI model
 * @param {Object} options - Query options
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useUsageByModelQuery = ({ queryOptions } = {}) => {
  return useQuery({
    queryKey: quotaKeys.usageByModel(),
    queryFn: async () => {
      const response = await axiosInstance.get('/api/observability/usage-by-model');
      return response.data;
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
    refetchOnWindowFocus: true,
    ...queryOptions,
  });
};

export default {
  useQuotaQuery,
  useUsageHistoryQuery,
  useUsageByConversationQuery,
  useUsageByModelQuery,
};
