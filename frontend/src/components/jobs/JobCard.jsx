import React from 'react';
import { JobStatusBadge } from './JobStatusBadge';
import { JobStatus, isJobProcessing } from '../../api/jobs/queries';

/**
 * JobCard - Card component displaying job information
 * @param {Object} props
 * @param {Object} props.job - Job data from the API
 * @param {Function} props.onCancel - Callback to cancel the job
 * @param {Function} props.onView - Callback to view job details
 * @param {boolean} props.showActions - Whether to show action buttons
 * @param {boolean} props.compact - Whether to use compact mode
 */
export const JobCard = ({ 
  job, 
  onCancel, 
  onView,
  showActions = true,
  compact = false,
}) => {
  if (!job) return null;

  const {
    jobId,
    status,
    question,
    createdAt,
    startedAt,
    completedAt,
    processingTimeSeconds,
    errorMessage,
    rowCount,
    cost,
  } = job;

  const formatDate = (dateString) => {
    if (!dateString) return '-';
    const date = new Date(dateString);
    return date.toLocaleString('vi-VN', {
      day: '2-digit',
      month: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  const formatDuration = (seconds) => {
    if (!seconds) return '-';
    if (seconds < 60) return `${seconds}s`;
    const minutes = Math.floor(seconds / 60);
    const remainingSeconds = seconds % 60;
    return `${minutes}m ${remainingSeconds}s`;
  };

  const formatCost = (costValue) => {
    if (!costValue) return '-';
    return `$${costValue.toFixed(4)}`;
  };

  const isProcessing = isJobProcessing(status);

  return (
    <div className={`job-card ${compact ? 'job-card-compact' : ''}`}>
      <div className="job-card-header">
        <JobStatusBadge status={status} size={compact ? 'sm' : 'md'} />
        <span className="job-id">#{jobId?.slice(0, 8)}</span>
      </div>

      <div className="job-card-body">
        <p className="job-question" title={question}>
          {question?.length > 100 ? `${question.slice(0, 100)}...` : question || '-'}
        </p>

        {!compact && (
          <>
            <div className="job-meta">
              <div className="meta-item">
                <span className="meta-label">Created:</span>
                <span className="meta-value">{formatDate(createdAt)}</span>
              </div>
              
              {startedAt && (
                <div className="meta-item">
                  <span className="meta-label">Started:</span>
                  <span className="meta-value">{formatDate(startedAt)}</span>
                </div>
              )}
              
              {completedAt && (
                <div className="meta-item">
                  <span className="meta-label">Completed:</span>
                  <span className="meta-value">{formatDate(completedAt)}</span>
                </div>
              )}
            </div>

            <div className="job-stats">
              {processingTimeSeconds !== null && (
                <div className="stat-item">
                  <span className="stat-label">Duration:</span>
                  <span className="stat-value">{formatDuration(processingTimeSeconds)}</span>
                </div>
              )}
              
              {rowCount !== null && (
                <div className="stat-item">
                  <span className="stat-label">Rows:</span>
                  <span className="stat-value">{rowCount}</span>
                </div>
              )}
              
              {cost !== null && (
                <div className="stat-item">
                  <span className="stat-label">Cost:</span>
                  <span className="stat-value">{formatCost(cost)}</span>
                </div>
              )}
            </div>

            {status === JobStatus.FAILED && errorMessage && (
              <div className="job-error">
                <span className="error-label">Error:</span>
                <span className="error-message">{errorMessage}</span>
              </div>
            )}
          </>
        )}
      </div>

      {showActions && (
        <div className="job-card-actions">
          {onView && (
            <button 
              className="btn btn-view" 
              onClick={() => onView(job)}
            >
              View
            </button>
          )}
          
          {isProcessing && onCancel && (
            <button 
              className="btn btn-cancel" 
              onClick={() => onCancel(jobId)}
            >
              Cancel
            </button>
          )}
        </div>
      )}

      <style>{`
        .job-card {
          background: #fff;
          border: 1px solid #e5e7eb;
          border-radius: 8px;
          padding: 16px;
          transition: box-shadow 0.2s ease;
        }

        .job-card:hover {
          box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
        }

        .job-card-compact {
          padding: 12px;
        }

        .job-card-header {
          display: flex;
          align-items: center;
          justify-content: space-between;
          margin-bottom: 12px;
        }

        .job-id {
          font-size: 12px;
          color: #9ca3af;
          font-family: monospace;
        }

        .job-card-body {
          margin-bottom: 12px;
        }

        .job-question {
          font-size: 14px;
          color: #374151;
          margin: 0 0 12px 0;
          line-height: 1.5;
        }

        .job-meta {
          display: flex;
          flex-wrap: wrap;
          gap: 16px;
          margin-bottom: 12px;
        }

        .meta-item {
          display: flex;
          gap: 6px;
          font-size: 12px;
        }

        .meta-label {
          color: #6b7280;
        }

        .meta-value {
          color: #374151;
          font-weight: 500;
        }

        .job-stats {
          display: flex;
          flex-wrap: wrap;
          gap: 16px;
          padding: 12px;
          background: #f9fafb;
          border-radius: 6px;
        }

        .stat-item {
          display: flex;
          gap: 6px;
          font-size: 13px;
        }

        .stat-label {
          color: #6b7280;
        }

        .stat-value {
          color: #374151;
          font-weight: 600;
        }

        .job-error {
          margin-top: 12px;
          padding: 12px;
          background: #fef2f2;
          border: 1px solid #fee2e2;
          border-radius: 6px;
        }

        .error-label {
          display: block;
          font-size: 12px;
          font-weight: 600;
          color: #b91c1c;
          margin-bottom: 4px;
        }

        .error-message {
          font-size: 13px;
          color: #991b1b;
        }

        .job-card-actions {
          display: flex;
          gap: 8px;
          justify-content: flex-end;
        }

        .btn {
          padding: 6px 14px;
          border-radius: 6px;
          font-size: 13px;
          font-weight: 500;
          cursor: pointer;
          border: none;
          transition: all 0.2s ease;
        }

        .btn-view {
          background: #f3f4f6;
          color: #374151;
        }

        .btn-view:hover {
          background: #e5e7eb;
        }

        .btn-cancel {
          background: #fee2e2;
          color: #b91c1c;
        }

        .btn-cancel:hover {
          background: #fecaca;
        }
      `}</style>
    </div>
  );
};

export default JobCard;
