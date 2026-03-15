/**
 * useJobs - Custom hooks for job queue integration
 * Provides a unified interface for managing background jobs
 */
import { useState, useCallback, useMemo } from 'react';
import {
  useJobsQuery,
  useJobQuery,
  isJobProcessing,
  isJobTerminal,
  JobStatus,
} from '../api/jobs/queries';
import {
  useCreateJobMutation,
  useCancelJobMutation,
} from '../api/jobs/commands';

/**
 * useJobs - Hook to get all jobs for the current user
 * @param {Object} options - Query options
 * @param {boolean} options.includeCompleted - Include completed jobs
 * @returns {Object} - { jobs, isLoading, error, refetch }
 */
export const useJobs = (options = {}) => {
  const { includeCompleted = false } = options;
  
  const { data: jobs = [], isLoading, error, refetch } = useJobsQuery(
    { includeCompleted },
    options
  );

  return {
    jobs,
    isLoading,
    error,
    refetch,
  };
};

/**
 * useJob - Hook to get a single job with automatic polling
 * @param {string} jobId - The job ID to monitor
 * @param {Object} options - Query options
 * @param {boolean} options.enabled - Enable/disable the query
 * @param {number} options.pollInterval - Custom polling interval (ms)
 * @param {Function} options.onComplete - Callback when job completes
 * @param {Function} options.onError - Callback when job fails
 * @returns {Object} - { job, isLoading, error, isPolling }
 */
export const useJob = (jobId, options = {}) => {
  const {
    enabled = true,
    pollInterval,
    onComplete,
    onError,
    ...queryOptions
  } = options;

  const { data: job, isLoading, error, isFetching } = useJobQuery(
    jobId,
    {
      enabled,
      refetchInterval: pollInterval,
      ...queryOptions,
    }
  );

  // Check if we're actively polling (for running/queued jobs)
  const isPolling = useMemo(() => {
    if (!job) return false;
    return isJobProcessing(job.status) && isFetching;
  }, [job, isFetching]);

  // Callbacks for job completion/failure
  const handleComplete = useCallback(() => {
    if (onComplete && job) {
      onComplete(job);
    }
  }, [job, onComplete]);

  const handleError = useCallback(() => {
    if (onError && job?.status === JobStatus.FAILED) {
      onError(job);
    }
  }, [job, onError]);

  // Watch for job completion
  if (job && isJobTerminal(job.status)) {
    if (job.status === JobStatus.FAILED) {
      handleError();
    } else {
      handleComplete();
    }
  }

  return {
    job,
    isLoading,
    error,
    isPolling,
    isRunning: job ? isJobProcessing(job.status) : false,
    isCompleted: job?.status === JobStatus.COMPLETED,
    isFailed: job?.status === JobStatus.FAILED,
    isCancelled: job?.status === JobStatus.CANCELLED,
  };
};

/**
 * useCreateJob - Hook to create a new background job
 * @param {Object} options - Mutation options
 * @param {Function} options.onSuccess - Callback on success
 * @param {Function} options.onError - Callback on error
 * @returns {Object} - { createJob, isCreating, error }
 */
export const useCreateJob = (options = {}) => {
  const { onSuccess, onError } = options;

  const { mutate: createJob, isPending: isCreating, error } = useCreateJobMutation({
    onSuccess: (data) => {
      if (onSuccess) {
        onSuccess(data);
      }
    },
    onError: (error) => {
      if (onError) {
        onError(error);
      }
    },
  });

  return {
    createJob,
    isCreating,
    error,
  };
};

/**
 * useCancelJob - Hook to cancel a running or queued job
 * @param {Object} options - Mutation options
 * @param {Function} options.onSuccess - Callback on success
 * @param {Function} options.onError - Callback on error
 * @returns {Object} - { cancelJob, isCancelling, error }
 */
export const useCancelJob = (options = {}) => {
  const { onSuccess, onError } = options;

  const { mutate: cancelJob, isPending: isCancelling, error } = useCancelJobMutation({
    onSuccess: (data) => {
      if (onSuccess) {
        onSuccess(data);
      }
    },
    onError: (error) => {
      if (onError) {
        onError(error);
      }
    },
  });

  return {
    cancelJob,
    isCancelling,
    error,
  };
};

/**
 * useJobWithPolling - Convenience hook to create and monitor a job
 * Creates a job and automatically polls until completion
 * @param {Object} options - Options
 * @param {Function} options.onSuccess - Callback on job completion
 * @param {Function} options.onError - Callback on job failure
 * @returns {Object} - All job-related functions and state
 */
export const useJobWithPolling = (options = {}) => {
  const { onSuccess, onError, pollInterval } = options;

  // State to track the current job ID
  const [currentJobId, setCurrentJobId] = useState(null);

  // Create job mutation
  const { createJob, isCreating: isCreatingJob, error: createError } = useCreateJob({
    onSuccess: (data) => {
      setCurrentJobId(data.jobId);
      if (options.onJobCreated) {
        options.onJobCreated(data.jobId);
      }
    },
  });

  // Job query with polling
  const { 
    job, 
    isLoading: isLoadingJob, 
    error: jobError,
    isPolling,
  } = useJob(currentJobId, {
    enabled: !!currentJobId,
    pollInterval,
    onComplete: (completedJob) => {
      if (onSuccess) {
        onSuccess(completedJob);
      }
    },
    onError: (failedJob) => {
      if (onError) {
        onError(failedJob);
      }
    },
  });

  // Cancel mutation
  const { cancelJob, isCancelling, error: cancelError } = useCancelJob();

  // Combined error state
  const error = createError || jobError || cancelError;

  // Submit a new job
  const submitJob = useCallback((question, connectionId) => {
    setCurrentJobId(null);
    createJob({ question, connectionId });
  }, [createJob]);

  // Check if job is in progress
  const isJobInProgress = !!(currentJobId && job && isJobProcessing(job.status));

  return {
    // Job state
    job,
    jobId: currentJobId,
    
    // Status
    isCreating: isCreatingJob,
    isLoading: isLoadingJob,
    isPolling,
    isCancelling,
    isJobInProgress,
    isCompleted: job?.status === JobStatus.COMPLETED,
    isFailed: job?.status === JobStatus.FAILED,
    
    // Actions
    submitJob,
    cancelJob: () => currentJobId && cancelJob(currentJobId),
    
    // Errors
    error,
  };
};

// Export all hooks
export default {
  useJobs,
  useJob,
  useCreateJob,
  useCancelJob,
  useJobWithPolling,
  // Re-export utilities
  JobStatus,
  isJobProcessing,
  isJobTerminal,
};
