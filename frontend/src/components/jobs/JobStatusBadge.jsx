import React from 'react';
import { JobStatus } from '../../api/jobs/queries';

/**
 * JobStatusBadge - Displays job status with appropriate styling
 * @param {Object} props
 * @param {string} props.status - Job status (queued, running, completed, failed, cancelled)
 * @param {string} props.size - Badge size: 'sm', 'md', 'lg'
 * @param {boolean} props.showIcon - Whether to show status icon
 */
export const JobStatusBadge = ({ 
  status, 
  size = 'md',
  showIcon = true,
}) => {
  const getStatusConfig = (jobStatus) => {
    switch (jobStatus) {
      case JobStatus.QUEUED:
        return {
          label: 'Queued',
          className: 'status-queued',
        };
      case JobStatus.RUNNING:
        return {
          label: 'Running',
          className: 'status-running',
        };
      case JobStatus.COMPLETED:
        return {
          label: 'Completed',
          className: 'status-completed',
        };
      case JobStatus.FAILED:
        return {
          label: 'Failed',
          className: 'status-failed',
        };
      case JobStatus.CANCELLED:
        return {
          label: 'Cancelled',
          className: 'status-cancelled',
        };
      default:
        return {
          label: jobStatus || 'Unknown',
          className: 'status-unknown',
        };
    }
  };

  const config = getStatusConfig(status);

  const getIcon = (jobStatus) => {
    switch (jobStatus) {
      case JobStatus.QUEUED:
        return (
          <svg className="status-icon" viewBox="0 0 16 16" fill="currentColor">
            <circle cx="8" cy="8" r="6" fillOpacity="0.3" />
            <circle cx="8" cy="8" r="3" />
          </svg>
        );
      case JobStatus.RUNNING:
        return (
          <svg className="status-icon animate-spin" viewBox="0 0 16 16" fill="currentColor">
            <path d="M8 0a8 8 0 100 16A8 8 0 008 0zm0 2a6 6 0 110 12A6 6 0 018 2zm-1 3v4h3" stroke="currentColor" strokeWidth="1.5" fill="none" />
          </svg>
        );
      case JobStatus.COMPLETED:
        return (
          <svg className="status-icon" viewBox="0 0 16 16" fill="currentColor">
            <path d="M6.5 12.5l-4-4 1.5-1.5 2.5 2.5 5.5-5.5 1.5 1.5-7 7z" />
          </svg>
        );
      case JobStatus.FAILED:
        return (
          <svg className="status-icon" viewBox="0 0 16 16" fill="currentColor">
            <path d="M8 0a8 8 0 100 16A8 8 0 008 0zm1 4h-2v4H5V4H3v2h2V2h2v4h2V4z" />
          </svg>
        );
      case JobStatus.CANCELLED:
        return (
          <svg className="status-icon" viewBox="0 0 16 16" fill="currentColor">
            <path d="M8 0a8 8 0 100 16A8 8 0 008 0zm3 8H5v2h6V8z" />
          </svg>
        );
      default:
        return null;
    }
  };

  const sizeClasses = {
    sm: 'badge-sm',
    md: 'badge-md',
    lg: 'badge-lg',
  };

  return (
    <span className={`job-status-badge ${config.className} ${sizeClasses[size]}`}>
      {showIcon && <span className="badge-icon-wrapper">{getIcon(status)}</span>}
      <span className="badge-label">{config.label}</span>

      <style>{`
        .job-status-badge {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          padding: 4px 10px;
          border-radius: 12px;
          font-size: 13px;
          font-weight: 500;
          line-height: 1.4;
        }

        .badge-sm {
          padding: 2px 8px;
          font-size: 11px;
        }

        .badge-lg {
          padding: 6px 14px;
          font-size: 14px;
        }

        .status-icon {
          width: 14px;
          height: 14px;
        }

        .badge-sm .status-icon {
          width: 12px;
          height: 12px;
        }

        .badge-lg .status-icon {
          width: 16px;
          height: 16px;
        }

        .status-queued {
          background-color: #e0f2fe;
          color: #0369a1;
        }

        .status-running {
          background-color: #fef3c7;
          color: #b45309;
        }

        .status-completed {
          background-color: #dcfce7;
          color: #15803d;
        }

        .status-failed {
          background-color: #fee2e2;
          color: #b91c1c;
        }

        .status-cancelled {
          background-color: #f3f4f6;
          color: #6b7280;
        }

        .status-unknown {
          background-color: #f3f4f6;
          color: #6b7280;
        }

        @keyframes spin {
          from {
            transform: rotate(0deg);
          }
          to {
            transform: rotate(360deg);
          }
        }

        .animate-spin {
          animation: spin 1s linear infinite;
        }
      `}</style>
    </span>
  );
};

export default JobStatusBadge;
