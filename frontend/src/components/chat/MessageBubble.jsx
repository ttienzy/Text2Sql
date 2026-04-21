import React, { useState, useEffect } from 'react';
import {
  Avatar,
  Typography,
  Space,
  Spin,
  Tooltip,
  Tag,
} from 'antd';
import {
  UserOutlined,
  RobotOutlined,
  LoadingOutlined,
  CopyOutlined,
  CheckOutlined,
  ClockCircleOutlined,
  TableOutlined,
  SendOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons';
import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';
import SqlBlock from './SqlBlock';
import ResultTable from './ResultTable';
import TokenInfo from './TokenInfo';
import EnhancedMessageInfo from './EnhancedMessageInfo';
import ConversationContextIndicator from './ConversationContextIndicator';
import TableSchemaButton from './TableSchemaButton';
import ForbiddenWarning from './ForbiddenWarning';
import { renderTableLinks, extractTableNames } from '../../utils/tableLinksRenderer';
import useConnectionStore from '../../store/connectionStore';

dayjs.extend(relativeTime);

const { Text } = Typography;

const typingMessages = [
  'Thinking...',
  'Analyzing your question...',
  'Building SQL query...',
  'Executing query...',
  'Processing results...',
];

const MessageBubble = ({ message, onSuggestedQueryClick, tableNames = [] }) => {
  const [copied, setCopied] = useState(false);
  const [typingIndex, setTypingIndex] = useState(0);
  const [isHovered, setIsHovered] = useState(false);
  const { activeConnection } = useConnectionStore();

  const isUser = message.role === 'user';
  const isPending = message.isPending || message.status === 'pending';

  const pipeline = message.pipeline;
  const isForbidden = pipeline === 'Forbidden';

  const detectedTables = !isUser && message.content
    ? extractTableNames(message.content, tableNames)
    : [];

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

  const formatTime = (timestamp) => {
    if (!timestamp) return '';
    return dayjs(timestamp).format('HH:mm');
  };

  const formatRelativeTime = (timestamp) => {
    if (!timestamp) return '';
    return dayjs(timestamp).fromNow();
  };

  useEffect(() => {
    if (isPending) {
      const interval = setInterval(() => {
        setTypingIndex(prev => (prev + 1) % typingMessages.length);
      }, 2000);
      return () => clearInterval(interval);
    }
  }, [isPending]);

  if (isPending) {
    return (
      <div
        style={{
          display: 'flex',
          flexDirection: 'row',
          alignItems: 'flex-start',
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
            padding: '16px 20px',
            borderRadius: 12,
            border: '1px solid #e8e8e8',
            boxShadow: '0 2px 8px rgba(0,0,0,0.08)',
            maxWidth: '80%',
          }}
        >
          <Space direction="vertical" size="small" style={{ minWidth: 200 }}>
            <Space>
              <Spin indicator={<LoadingOutlined style={{ fontSize: 18, color: '#1890ff' }} spin />} />
              <Text strong style={{ color: '#1890ff', fontSize: 14 }}>
                {typingMessages[typingIndex]}
              </Text>
            </Space>
            
            <div style={{
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              marginTop: 8,
            }}>
              <div style={{
                display: 'flex',
                gap: 4,
              }}>
                {[0, 1, 2].map(i => (
                  <div
                    key={i}
                    style={{
                      width: 6,
                      height: 6,
                      borderRadius: '50%',
                      backgroundColor: typingIndex >= i ? '#1890ff' : '#d9d9d9',
                      transition: 'background-color 0.3s',
                    }}
                  />
                ))}
              </div>
              <Text type="secondary" style={{ fontSize: 12 }}>
                {activeConnection?.name || 'Connecting...'}
              </Text>
            </div>

            <div style={{
              marginTop: 8,
              padding: '8px 12px',
              backgroundColor: '#f0f5ff',
              borderRadius: 6,
              border: '1px solid #d6e4ff',
            }}>
              <Space size="small">
                <ThunderboltOutlined style={{ color: '#1890ff' }} />
                <Text type="secondary" style={{ fontSize: 12 }}>
                  Your query is being processed
                </Text>
              </Space>
            </div>
          </Space>
        </div>
      </div>
    );
  }

  return (
    <div
      className="message-bubble"
      style={{
        display: 'flex',
        flexDirection: isUser ? 'row-reverse' : 'row',
        alignItems: 'flex-start',
        marginBottom: 16,
      }}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
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
        className="message-bubble-content"
        style={{
          backgroundColor: isUser ? '#f0f5ff' : '#ffffff',
          padding: '12px 16px',
          borderRadius: 8,
          border: `1px solid ${isForbidden ? '#ff4d4f' : (isUser ? '#d6e4ff' : '#e8e8e8')}`,
          boxShadow: isHovered ? '0 2px 8px rgba(0,0,0,0.1)' : '0 1px 2px rgba(0,0,0,0.05)',
          maxWidth: '80%',
          minWidth: 200,
          transform: isHovered ? 'translateY(-1px)' : 'translateY(0)',
          transition: 'all 0.15s ease-in-out',
        }}
      >
        {isForbidden && message.content && (
          <ForbiddenWarning message={message.content} />
        )}

        {!isUser && message.contextEntities && (
          <ConversationContextIndicator
            contextEntities={message.contextEntities || []}
            primaryEntity={message.primaryEntity}
            show={true}
          />
        )}

        {message.content && !isForbidden && (
          <div style={{ marginBottom: message.sqlQuery ? 12 : 0 }}>
            <Text style={{
              whiteSpace: 'pre-wrap',
              fontSize: 14,
              color: isUser ? 'inherit' : '#262626',
            }}>
              {!isUser && tableNames.length > 0
                ? renderTableLinks(message.content, tableNames)
                : message.content
              }
            </Text>
          </div>
        )}

        {!isUser && detectedTables.length > 0 && (
          <div style={{
            marginTop: 8,
            marginBottom: 8,
            padding: '8px 12px',
            backgroundColor: '#f0f5ff',
            borderRadius: 4,
            border: '1px solid #d6e4ff',
          }}>
            <Space size={4} wrap>
              <TableOutlined style={{ color: '#1890ff' }} />
              <Text type="secondary" style={{ fontSize: 12 }}>
                Referenced tables:
              </Text>
              {detectedTables.map(tableName => (
                <Tag key={tableName} color="blue" style={{ margin: 0 }}>
                  {tableName}
                </Tag>
              ))}
            </Space>
            <div style={{ marginTop: 8 }}>
              {detectedTables.map(tableName => (
                <TableSchemaButton
                  key={`schema-${tableName}`}
                  tableName={tableName}
                  size="small"
                />
              ))}
            </div>
          </div>
        )}

        {(message.sqlQuery || message.SqlQuery) && (
          <SqlBlock sql={message.sqlQuery || message.SqlQuery} />
        )}

        {results && results.length > 0 && (
          <ResultTable
            data={results}
            rowCount={message.rowCount || message.RowCount}
          />
        )}

        {message.chartImageBase64 && (
          <div style={{ marginTop: 12, marginBottom: 12, overflow: 'hidden' }}>
            <Text strong style={{ display: 'block', marginBottom: 8, color: '#1890ff', fontSize: 13 }}>
              Data Visualization ({message.chartType || 'Chart'})
            </Text>
            <img 
              src={`data:image/png;base64,${message.chartImageBase64}`} 
              alt={`${message.chartType || 'Data'} Chart`}
              style={{
                maxWidth: '100%',
                maxHeight: '400px',
                borderRadius: 8,
                border: '1px solid #f0f0f0',
                boxShadow: '0 2px 8px rgba(0,0,0,0.05)',
                objectFit: 'contain'
              }} 
            />
          </div>
        )}

        {(message.totalTokens || message.TotalTokens) && (
          <TokenInfo
            inputTokens={message.inputTokens || message.InputTokens}
            outputTokens={message.outputTokens || message.OutputTokens}
            totalTokens={message.totalTokens || message.TotalTokens}
            cost={message.cost || message.Cost}
            model={message.model || message.Model}
          />
        )}

        <EnhancedMessageInfo
          message={message}
          onSuggestedQueryClick={onSuggestedQueryClick}
        />

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

        {message.isOptimistic && (
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