import React, { useState } from 'react';
import { 
  Avatar, 
  Typography, 
  Space, 
  Spin, 
  Tooltip,
} from 'antd';
import { 
  UserOutlined, 
  RobotOutlined, 
  LoadingOutlined,
  CopyOutlined,
  CheckOutlined,
  ClockCircleOutlined,
} from '@ant-design/icons';
import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';
import SqlBlock from './SqlBlock';
import ResultTable from './ResultTable';
import TokenInfo from './TokenInfo';
import { escapeHtml } from '../../utils/security';
import { extractErrorMessage, hasError } from '../../utils/errorHandler';

dayjs.extend(relativeTime);

const { Text } = Typography;

/**
 * MessageBubble - Component for displaying user/assistant messages
 * @param {Object} props
 * @param {Object} props.message - The message object
 * @param {boolean} props.isLast - Whether this is the last message in the conversation
 */
const MessageBubble = ({ message }) => {
  const [copied, setCopied] = useState(false);
  
  const isUser = message.role === 'user';
  const isPending = message.isPending || message.status === 'pending';
  const isOptimistic = message.isOptimistic;
  const errorMessage = extractErrorMessage(message);
  const hasErrorState = hasError(message);
  
  // Parse results if available
  let results = null;
  if (message.results) {
    try {
      results = typeof message.results === 'string' 
        ? JSON.parse(message.results) 
        : message.results;
    } catch (e) {
      console.warn('Failed to parse results:', e);
    }
  }
  
  const handleCopy = async () => {
    const textToCopy = message.content || message.sqlQuery || '';
    await navigator.clipboard.writeText(textToCopy);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };
  
  // Format timestamp
  const formatTime = (timestamp) => {
    if (!timestamp) return '';
    const date = dayjs(timestamp);
    return date.format('HH:mm');
  };
  
  const formatRelativeTime = (timestamp) => {
    if (!timestamp) return '';
    return dayjs(timestamp).fromNow();
  };

  // Loading/pending state
  if (isPending) {
    return (
      <div
        style={{
          display: 'flex',
          flexDirection: 'row',
          alignItems: 'flex-start',
          justifyContent: 'flex-start',
          marginBottom: 16,
        }}
      >
        <Avatar
          icon={<RobotOutlined />}
          style={{ 
            backgroundColor: '#52c41a',
            marginRight: 8,
          }}
        />
        <div
          style={{
            backgroundColor: '#ffffff',
            padding: '12px 16px',
            borderRadius: 8,
            border: '1px solid #e8e8e8',
            boxShadow: '0 1px 2px rgba(0,0,0,0.05)',
            maxWidth: '80%',
          }}
        >
          <Space>
            <Spin indicator={<LoadingOutlined style={{ fontSize: 16 }} spin />} />
            <Text type="secondary">Processing your question...</Text>
          </Space>
        </div>
      </div>
    );
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: isUser ? 'row-reverse' : 'row',
        alignItems: 'flex-start',
        marginBottom: 16,
      }}
    >
      <Avatar
        icon={isUser ? <UserOutlined /> : <RobotOutlined />}
        style={{ 
          backgroundColor: isUser ? '#1890ff' : '#52c41a',
          marginLeft: isUser ? 8 : 0,
          marginRight: isUser ? 0 : 8,
          flexShrink: 0,
        }}
      />
      
      <div
        style={{
          backgroundColor: isUser ? '#f0f5ff' : '#ffffff',
          padding: '12px 16px',
          borderRadius: 8,
          border: `1px solid ${hasErrorState ? '#ffccc7' : (isUser ? '#d6e4ff' : '#e8e8e8')}`,
          boxShadow: '0 1px 2px rgba(0,0,0,0.05)',
          maxWidth: '80%',
          minWidth: 200,
        }}
      >
        {/* Error message display */}
        {hasErrorState && (
          <div 
            style={{ 
              marginBottom: 8, 
              padding: '8px 12px', 
              backgroundColor: '#fff2f0', 
              borderRadius: 4,
              border: '1px solid #ffccc7',
            }}
          >
            <Text type="danger" style={{ fontSize: 13 }}>
              Error: {errorMessage}
            </Text>
          </div>
        )}
        
        {/* User/Assistant text content - escape HTML for security */}
        {message.content && (
          <div style={{ marginBottom: message.sqlQuery ? 12 : 0 }}>
            <Text style={{ 
              whiteSpace: 'pre-wrap', 
              fontSize: 14,
              color: isUser ? 'inherit' : '#262626',
            }}>
              {/* Use escapeHtml to prevent XSS */}
              {escapeHtml(message.content)}
            </Text>
          </div>
        )}
        
        {/* SQL Query Block */}
        {(message.sqlQuery || message.SqlQuery) && (
          <SqlBlock 
            sql={message.sqlQuery || message.SqlQuery} 
            error={hasErrorState}
          />
        )}
        
        {/* Results Table */}
        {results && results.length > 0 && (
          <ResultTable 
            data={results} 
            rowCount={message.rowCount || message.RowCount}
          />
        )}
        
        {/* Token Info */}
        {(message.totalTokens || message.TotalTokens) && (
          <TokenInfo 
            inputTokens={message.inputTokens || message.InputTokens}
            outputTokens={message.outputTokens || message.OutputTokens}
            totalTokens={message.totalTokens || message.TotalTokens}
            cost={message.cost || message.Cost}
            model={message.model || message.Model}
          />
        )}
        
        {/* Footer: Copy button and timestamp */}
        {!isUser && (message.content || message.sqlQuery) && (
          <div 
            style={{ 
              marginTop: 8, 
              paddingTop: 8, 
              borderTop: '1px dashed #d9d9d9',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
            }}
          >
            <Tooltip title={copied ? 'Copied!' : 'Copy content'}>
              <span 
                onClick={handleCopy}
                style={{ 
                  cursor: 'pointer', 
                  color: copied ? '#52c41a' : '#8c8c8c',
                  fontSize: 12,
                }}
              >
                {copied ? <CheckOutlined /> : <CopyOutlined />} 
                {copied ? ' Copied' : ' Copy'}
              </span>
            </Tooltip>
            
            {message.createdAt && (
              <Tooltip title={formatRelativeTime(message.createdAt)}>
                <Space size={4} style={{ color: '#8c8c8c', fontSize: 12 }}>
                  <ClockCircleOutlined />
                  <span>{formatTime(message.createdAt)}</span>
                </Space>
              </Tooltip>
            )}
          </div>
        )}
        
        {/* Timestamp for user messages */}
        {isUser && message.createdAt && (
          <div 
            style={{ 
              marginTop: 8,
              textAlign: 'right',
            }}
          >
            <Tooltip title={formatRelativeTime(message.createdAt)}>
              <Space size={4} style={{ color: '#8c8c8c', fontSize: 12 }}>
                <ClockCircleOutlined />
                <span>{formatTime(message.createdAt)}</span>
              </Space>
            </Tooltip>
          </div>
        )}
        
        {/* Optimistic indicator */}
        {isOptimistic && (
          <div style={{ marginTop: 4 }}>
            <Text type="secondary" style={{ fontSize: 11 }}>
              (Sending...)
            </Text>
          </div>
        )}
      </div>
    </div>
  );
};

export default MessageBubble;
