/**
 * Job Commands - CQRS Pattern
 * React Query mutation hooks for job operations (create, cancel)
 */
import { useMutation, useQueryClient } from '@tanstack/react-query';
import axiosInstance from '../axios';
import { API_ENDPOINTS } from '../../constants';
import { jobKeys } from './queries';

/**
 * useCreateJobMutation - Create a new background job
 * Use this for long-running queries that should be processed asynchronously
 * @param {Object} options - Mutation options
 * @param {Function} options.onSuccess - Callback on success
 * @param {Function} options.onError - Callback on error
 */
export const useCreateJobMutation = ({ onSuccess, onError } = {}) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async ({ question, connectionId }) => {
      const response = await axiosInstance.post(API_ENDPOINTS.JOBS, {
        Question: question,
        ConnectionId: connectionId,
      });
      return response.data;
    },
    onSuccess: (data, variables) => {
      // Invalidate the jobs list to include the new job
      queryClient.invalidateQueries({ queryKey: jobKeys.lists() });

      // Set the new job data in the cache
      queryClient.setQueryData(jobKeys.detail(data.jobId), {
        jobId: data.jobId,
        status: data.status,
        question: variables.question,
        createdAt: new Date().toISOString(),
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

/**
 * useCancelJobMutation - Cancel a running or queued job
 * @param {Object} options - Mutation options
 * @param {Function} options.onSuccess - Callback on success
 * @param {Function} options.onError - Callback on error
 */
export const useCancelJobMutation = ({ onSuccess, onError } = {}) => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (jobId) => {
      const response = await axiosInstance.post(`${API_ENDPOINTS.JOBS}/${jobId}/cancel`);
      return response.data;
    },
    onSuccess: (data, jobId) => {
      // Update the job in cache to show cancelled status
      const currentJob = queryClient.getQueryData(jobKeys.detail(jobId));
      if (currentJob) {
        queryClient.setQueryData(jobKeys.detail(jobId), {
          ...currentJob,
          status: 'cancelled',
        });
      }

      // Invalidate the jobs list to reflect the change
      queryClient.invalidateQueries({ queryKey: jobKeys.lists() });

      // Call custom onSuccess callback
      if (onSuccess) {
        onSuccess(data, jobId);
      }
    },
    onError: (error, jobId) => {
      // Call custom onError callback
      if (onError) {
        onError(error, jobId);
      }
    },
  });
};

export default {
  useCreateJobMutation,
  useCancelJobMutation,
};
