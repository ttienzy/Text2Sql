import { Space, Typography, Tag, Tooltip } from 'antd';
import {
    MessageOutlined,
    ClockCircleOutlined,
    LinkOutlined,
    InfoCircleOutlined
} from '@ant-design/icons';

const { Text } = Typography;

/**
 * ConversationStatus - Hiển thị trạng thái conversation ở dưới chat input
 * Cho biết số lượng messages, conversation mode, v.v.
 */
const ConversationStatus = ({
    conversationId = null,
    messageCount = 0,
    isConversationMode = false,
    lastMessageTime = null,
    compact = false
}) => {
    if (!conversationId && messageCount === 0) {
        return null;
    }

    const formatTime = (timeString) => {
        if (!timeString) return null;
        const time = new Date(timeString);
        const now = new Date();
        const diffMinutes = Math.floor((now - time) / (1000 * 60));

        if (diffMinutes < 1) return 'just now';
        if (diffMinutes < 60) return `${diffMinutes}m ago`;
        if (diffMinutes < 1440) return `${Math.floor(diffMinutes / 60)}h ago`;
        return time.toLocaleDateString();
    };

    if (compact) {
        return (
            <div style={{
                padding: '4px 8px',
                backgroundColor: '#f8f9fa',
                borderRadius: 4,
                fontSize: 11,
                color: '#666'
            }}>
                <Space size={8}>
                    <span>
                        <MessageOutlined style={{ marginRight: 4 }} />
                        {messageCount} messages
                    </span>
                    {isConversationMode && (
                        <Tag size="small" color="blue" style={{ fontSize: 10, margin: 0 }}>
                            Context Mode
                        </Tag>
                    )}
                    {lastMessageTime && (
                        <span>
                            <ClockCircleOutlined style={{ marginRight: 4 }} />
                            {formatTime(lastMessageTime)}
                        </span>
                    )}
                </Space>
            </div>
        );
    }

    return (
        <div style={{
            padding: '8px 12px',
            backgroundColor: '#f8f9fa',
            borderTop: '1px solid #e8e8e8',
            borderRadius: '0 0 8px 8px'
        }}>
            <Space direction="vertical" size={4} style={{ width: '100%' }}>
                <Space size={12}>
                    <Space size={4}>
                        <MessageOutlined style={{ color: '#1890ff', fontSize: 12 }} />
                        <Text style={{ fontSize: 12 }}>
                            {messageCount} {messageCount === 1 ? 'message' : 'messages'}
                        </Text>
                    </Space>

                    {isConversationMode && (
                        <Tooltip title="Agent is using conversation history to provide better responses">
                            <Tag
                                icon={<LinkOutlined />}
                                color="blue"
                                size="small"
                                style={{ fontSize: 10, margin: 0 }}
                            >
                                Context-Aware Mode
                            </Tag>
                        </Tooltip>
                    )}

                    {lastMessageTime && (
                        <Space size={4}>
                            <ClockCircleOutlined style={{ color: '#8c8c8c', fontSize: 12 }} />
                            <Text type="secondary" style={{ fontSize: 12 }}>
                                Last: {formatTime(lastMessageTime)}
                            </Text>
                        </Space>
                    )}
                </Space>

                {conversationId && (
                    <Space size={4}>
                        <InfoCircleOutlined style={{ color: '#8c8c8c', fontSize: 11 }} />
                        <Text type="secondary" style={{ fontSize: 11, fontFamily: 'monospace' }}>
                            ID: {conversationId.substring(0, 8)}...
                        </Text>
                    </Space>
                )}
            </Space>
        </div>
    );
};

export default ConversationStatus;