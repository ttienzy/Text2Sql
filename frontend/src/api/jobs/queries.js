/**
 * Job Queries - CQRS Pattern
 * React Query hooks for fetching job data from the backend job queue
 */
import { useQuery } from '@tanstack/react-query';
import axiosInstance from '../axios';
import { API_ENDPOINTS } from '../../constants';

/**
 * Job status enum (matching backend)
 */
export const JobStatus = {
  QUEUED: 'queued',
  RUNNING: 'running',
  COMPLETED: 'completed',
  FAILED: 'failed',
  CANCELLED: 'cancelled',
};

/**
 * Query keys for job-related queries
 */
export const jobKeys = {
  all: ['jobs'],
  lists: () => [...jobKeys.all, 'list'],
  list: (includeCompleted) => [...jobKeys.lists(), { includeCompleted }],
  details: () => [...jobKeys.all, 'detail'],
  detail: (id) => [...jobKeys.details(), id],
};

/**
 * useJobsQuery - Get all jobs for the current user
 * @param {Object} options - Query options
 * @param {boolean} options.includeCompleted - Include completed jobs in the list
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useJobsQuery = (
  { includeCompleted = false } = {},
  queryOptions = {}
) => {
  return useQuery({
    queryKey: jobKeys.list(includeCompleted),
    queryFn: async () => {
      const response = await axiosInstance.get(API_ENDPOINTS.JOBS, {
        params: { includeCompleted },
      });
      return response.data;
    },
    staleTime: 30 * 1000, // 30 seconds - jobs don't change frequently
    ...queryOptions,
  });
};

/**
 * useJobQuery - Get a single job by ID with optional polling
 * @param {string} jobId - The job ID
 * @param {Object} options - Query options
 * @param {boolean} options.enabled - Whether to enable the query
 * @param {number} options.refetchInterval - Polling interval in ms (for running jobs)
 * @param {Object} options.queryOptions - Additional react-query options
 */
export const useJobQuery = (
  jobId,
  {
    enabled = true,
    refetchInterval: customRefetchInterval,
    ...queryOptions
  } = {}
) => {
  // Determine polling interval based on job status
  const getRefetchInterval = (data) => {
    if (customRefetchInterval !== undefined) {
      return customRefetchInterval;
    }
    // Poll every 2 seconds for running/queued jobs, don't poll for completed ones
    if (!data || data.status === JobStatus.RUNNING || data.status === JobStatus.QUEUED) {
      return 2000;
    }
    // Don't poll for terminal states
    return false;
  };

  return useQuery({
    queryKey: jobKeys.detail(jobId),
    queryFn: async () => {
      const response = await axiosInstance.get(`${API_ENDPOINTS.JOBS}/${jobId}`);
      return response.data;
    },
    enabled: !!jobId && enabled,
    refetchInterval: getRefetchInterval,
    // Retry failed requests 3 times
    retry: 3,
    retryDelay: 1000,
    ...queryOptions,
  });
};

/**
 * Check if job is in a terminal state
 * @param {string} status - Job status
 */
export const isJobTerminal = (status) => {
  return [
    JobStatus.COMPLETED,
    JobStatus.FAILED,
    JobStatus.CANCELLED,
  ].includes(status);
};

/**
 * Check if job is still processing
 * @param {string} status - Job status
 */
export const isJobProcessing = (status) => {
  return [
    JobStatus.QUEUED,
    JobStatus.RUNNING,
  ].includes(status);
};

export default {
  useJobsQuery,
  useJobQuery,
  JobStatus,
  jobKeys,
  isJobTerminal,
  isJobProcessing,
};
