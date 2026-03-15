import React from 'react';
import { Alert, Button, Space, Typography } from 'antd';
import { 
  InfoCircleOutlined, 
  WarningOutlined, 
  CloseCircleOutlined, 
  ExclamationCircleOutlined,
  RedoOutlined,
} from '@ant-design/icons';
import { extractErrorMessage, ErrorSeverity } from '../../utils/errorHandler';

const { Text, Title } = Typography;

/**
 * ErrorDisplay - Reusable component for displaying error messages
 * Supports different severity levels and optional retry action
 * 
 * @param {Object} props
 * @param {string|Error|Object} props.error - The error to display
 * @param {string} [props.severity] - Severity level: 'info', 'warning', 'error', 'critical'
 * @param {string} [props.title] - Custom title for the error
 * @param {boolean} [props.showRetry] - Whether to show retry button
 * @param {Function} [props.onRetry] - Callback when retry is clicked
 * @param {boolean} [props.fullPage] - Whether to display as full page error
 * @param {string} [props.className] - Additional CSS class
 */
const ErrorDisplay = ({ 
  error,
  severity: propSeverity,
  title,
  showRetry = false,
  onRetry,
  fullPage = false,
  className = '',
}) => {
  // Extract message and determine severity
  const message = extractErrorMessage(error);
  
  // Determine severity from prop or error
  const severity = propSeverity || error?.severity || ErrorSeverity.ERROR;
  
  // Get icon and style based on severity
  const getSeverityConfig = (level) => {
    switch (level) {
      case ErrorSeverity.INFO:
        return {
          icon: <InfoCircleOutlined />,
          alertType: 'info',
          color: '#1890ff',
          bgColor: '#e6f7ff',
          borderColor: '#91d5ff',
        };
      case ErrorSeverity.WARNING:
        return {
          icon: <WarningOutlined />,
          alertType: 'warning',
          color: '#faad14',
          bgColor: '#fffbe6',
          borderColor: '#ffe58f',
        };
      case ErrorSeverity.CRITICAL:
        return {
          icon: <ExclamationCircleOutlined />,
          alertType: 'error',
          color: '#cf1322',
          bgColor: '#fff1f0',
          borderColor: '#ffa39e',
        };
      case ErrorSeverity.ERROR:
      default:
        return {
          icon: <CloseCircleOutlined />,
          alertType: 'error',
          color: '#ff4d4f',
          bgColor: '#fff2f0',
          borderColor: '#ffccc7',
        };
    }
  };
  
  const config = getSeverityConfig(severity);
  
  // Full page error display
  if (fullPage) {
    return (
      <div 
        style={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          minHeight: fullPage ? '60vh' : 'auto',
          padding: fullPage ? 48 : 16,
          textAlign: 'center',
        }}
        className={className}
      >
        <div 
          style={{
            fontSize: 48,
            color: config.color,
            marginBottom: 16,
          }}
        >
          {config.icon}
        </div>
        <Title level={4} style={{ color: config.color, marginBottom: 8 }}>
          {title || 'Something went wrong'}
        </Title>
        <Text type="secondary" style={{ fontSize: 14, maxWidth: 400, marginBottom: 24 }}>
          {message}
        </Text>
        {showRetry && onRetry && (
          <Button 
            type="primary" 
            icon={<RedoOutlined />} 
            onClick={onRetry}
            size="large"
          >
            Try Again
          </Button>
        )}
      </div>
    );
  }
  
  // Inline error display (default)
  return (
    <Alert
      message={
        <Space direction="vertical" size={0} style={{ width: '100%' }}>
          {title && (
            <Text strong style={{ color: config.color }}>
              {title}
            </Text>
          )}
          <Text>{message}</Text>
        </Space>
      }
      icon={config.icon}
      type={config.alertType}
      showIcon
      style={{
        backgroundColor: config.bgColor,
        borderColor: config.borderColor,
      }}
      className={className}
      action={
        showRetry && onRetry ? (
          <Button size="small" onClick={onRetry}>
            <RedoOutlined /> Retry
          </Button>
        ) : undefined
      }
    />
  );
};

/**
 * InlineError - Compact error message for forms and small spaces
 * 
 * @param {Object} props
 * @param {string|Error|Object} props.error - The error to display
 * @param {string} [props.field] - Optional field name for context
 */
export const InlineError = ({ error, field }) => {
  const message = extractErrorMessage(error);
  
  if (!message) return null;
  
  return (
    <Text type="danger" style={{ fontSize: 12, display: 'block', marginTop: 4 }}>
      {field && `${field}: `}{message}
    </Text>
  );
};

/**
 * ErrorBoundary fallback component
 */
export const ErrorFallback = ({ error, resetError }) => {
  return (
    <ErrorDisplay
      error={error}
      severity={ErrorSeverity.CRITICAL}
      title="Application Error"
      showRetry={!!resetError}
      onRetry={resetError}
      fullPage
    />
  );
};

export default ErrorDisplay;
