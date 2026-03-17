import { Tag, Tooltip, Space } from 'antd';
import {
    MessageOutlined,
    LinkOutlined,
    StarOutlined,
    ClockCircleOutlined
} from '@ant-design/icons';

/**
 * ConversationIndicator - Hiển thị các indicator về conversation context
 * Cho biết message có phải là follow-up, có context, v.v.
 */
const ConversationIndicator = ({
    isFollowUp = false,
    hasContext = false,
    turnNumber = null,
    conversationId = null,
    size = 'small'
}) => {
    if (!isFollowUp && !hasContext && !turnNumber) {
        return null;
    }

    return (
        <Space size={4} style={{ marginTop: 4 }}>
            {/* Follow-up indicator */}
            {isFollowUp && (
                <Tooltip title="This is a follow-up question that builds on previous context">
                    <Tag
                        icon={<LinkOutlined />}
                        color="blue"
                        size={size}
                        style={{ fontSize: 10, margin: 0 }}
                    >
                        Follow-up
                    </Tag>
                </Tooltip>
            )}

            {/* Context indicator */}
            {hasContext && (
                <Tooltip title="This response uses conversation history for better context">
                    <Tag
                        icon={<MessageOutlined />}
                        color="green"
                        size={size}
                        style={{ fontSize: 10, margin: 0 }}
                    >
                        Context-aware
                    </Tag>
                </Tooltip>
            )}

            {/* Turn number */}
            {turnNumber && (
                <Tooltip title={`Turn ${turnNumber} in this conversation`}>
                    <Tag
                        icon={<ClockCircleOutlined />}
                        color="default"
                        size={size}
                        style={{ fontSize: 10, margin: 0 }}
                    >
                        Turn {turnNumber}
                    </Tag>
                </Tooltip>
            )}

            {/* First message indicator */}
            {turnNumber === 1 && (
                <Tooltip title="This is the first message in the conversation">
                    <Tag
                        icon={<StarOutlined />}
                        color="gold"
                        size={size}
                        style={{ fontSize: 10, margin: 0 }}
                    >
                        New
                    </Tag>
                </Tooltip>
            )}
        </Space>
    );
};

export default ConversationIndicator;