import React from 'react';
import { Alert, Button, Space, Typography } from 'antd';
import { PlusCircleOutlined, WarningOutlined } from '@ant-design/icons';

const { Text } = Typography;

/**
 * ConversationLimitBanner
 * Shown when a conversation has reached the 50-message context limit.
 *
 * @param {Object}   props
 * @param {boolean}  props.visible          - Whether to render the banner
 * @param {number}   props.totalCount       - Total number of messages in the conversation
 * @param {number}   props.messageLimit     - Configured limit (default 50)
 * @param {Function} props.onNewConversation - Callback to start a new conversation
 */
const ConversationLimitBanner = ({
  visible = false,
  totalCount = 0,
  messageLimit = 50,
  onNewConversation,
}) => {
  if (!visible) return null;

  return (
    <Alert
      type="warning"
      showIcon
      icon={<WarningOutlined />}
      style={{
        margin: '8px 16px',
        borderRadius: 8,
        border: '1px solid #faad14',
      }}
      message={
        <Space wrap>
          <Text strong style={{ color: '#d46b08' }}>
            Context limit reached ({totalCount}/{messageLimit} messages)
          </Text>
          <Text type="secondary" style={{ fontSize: 13 }}>
            AI context window is full. Please start a new conversation to continue querying.
          </Text>
        </Space>
      }
      action={
        <Button
          type="primary"
          size="small"
          danger
          icon={<PlusCircleOutlined />}
          onClick={onNewConversation}
          style={{ whiteSpace: 'nowrap' }}
        >
          New Conversation
        </Button>
      }
      banner
    />
  );
};

export default ConversationLimitBanner;
