import React, { useState } from 'react';
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
import { escapeHtml } from '../../utils/security';
import { extractErrorMessage, hasError } from '../../utils/errorHandler';
import { renderTableLinks, extractTableNames } from '../../utils/tableLinksRenderer';
import useConnectionStore from '../../store/connectionStore';

dayjs.extend(relativeTime);

const { Text } = Typography;

/**
 * MessageBubble - Component for displaying user/assistant messages
 * @param {Object} props
 * @param {Object} props.message - The message object
 * @param {Function} props.onSuggestedQueryClick - Callback for suggested query clicks
 * @param {Array<string>} props.tableNames - List of table names for link detection
 */
const MessageBubble = ({ message, onSuggestedQueryClick, tableNames = [] }) => {
  const [copied, setCopied] = useState(false);
  const { activeConnection } = useConnectionStore();

  const isUser = message.role === 'user';
  const isPending = message.isPending || message.status === 'pending';
  const isOptimistic = message.isOptimistic;
  const errorMessage = extractErrorMessage(message);
  const hasErrorState = hasError(message);

  // Check pipeline type for conditional rendering
  const pipeline = message.pipeline;
  
  // Check if this is a forbidden operation response
  const isForbidden = pipeline === 'Forbidden';

  // Detect table names in message content
  const detectedTables = !isUser && message.content
    ? extractTableNames(message.content, tableNames)
    : [];

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
          border: `1px solid ${isForbidden ? '#ff4d4f' : (hasErrorState ? '#ffccc7' : (isUser ? '#d6e4ff' : '#e8e8e8'))}`,
          boxShadow: '0 1px 2px rgba(0,0,0,0.05)',
          maxWidth: '80%',
          minWidth: 200,
        }}
      >
        {/* Forbidden operation warning - Use Ant Design Alert */}
        {isForbidden && message.content && (
          <ForbiddenWarning message={message.content} />
        )}

        {/* Error message display - only for real errors, not forbidden */}
        {hasErrorState && !isForbidden && (
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

        {/* Context Indicator - Show when AI used context to understand pronouns */}
        {!isUser && message.contextEntities && (
          <ConversationContextIndicator
            contextEntities={message.contextEntities || []}
            primaryEntity={message.primaryEntity}
            show={true}
          />
        )}

        {/* User/Assistant text content - with table links */}
        {/* Don't show content if it's forbidden (already shown in ForbiddenWarning) */}
        {message.content && !isForbidden && (
          <div style={{ marginBottom: message.sqlQuery ? 12 : 0 }}>
            <Text style={{
              whiteSpace: 'pre-wrap',
              fontSize: 14,
              color: isUser ? 'inherit' : '#262626',
            }}>
              {/* Render table links for assistant messages */}
              {!isUser && tableNames.length > 0
                ? renderTableLinks(message.content, tableNames)
                : escapeHtml(message.content)
              }
            </Text>
          </div>
        )}

        {/* Table references indicator */}
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

        {/* Enhanced Message Info - Processing steps, suggestions, etc. */}
        <EnhancedMessageInfo
          message={message}
          onSuggestedQueryClick={onSuggestedQueryClick}
        />

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
