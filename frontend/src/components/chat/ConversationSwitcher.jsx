import { useState } from 'react';
import { Button, Dropdown, Space, Typography, Tag, Tooltip } from 'antd';
import {
    SwapOutlined,
    MessageOutlined,
    ClockCircleOutlined,
    LinkOutlined,
    DownOutlined
} from '@ant-design/icons';

const { Text } = Typography;

/**
 * ConversationSwitcher - Dropdown để chuyển đổi giữa các conversation
 * Hiển thị conversation mode và thông tin context
 */
const ConversationSwitcher = ({
    conversations = [],
    currentConversation = null,
    onConversationSelect,
    onNewConversation,
    loading = false
}) => {
    const [dropdownVisible, setDropdownVisible] = useState(false);

    const formatTime = (timeString) => {
        if (!timeString) return 'Unknown';
        const time = new Date(timeString);
        const now = new Date();
        const diffMinutes = Math.floor((now - time) / (1000 * 60));

        if (diffMinutes < 1) return 'just now';
        if (diffMinutes < 60) return `${diffMinutes}m ago`;
        if (diffMinutes < 1440) return `${Math.floor(diffMinutes / 60)}h ago`;
        return time.toLocaleDateString();
    };

    const menuItems = [
        // New conversation option
        {
            key: 'new',
            label: (
                <div style={{ padding: '4px 0', borderBottom: '1px solid #f0f0f0', marginBottom: 4 }}>
                    <Button
                        type="primary"
                        size="small"
                        block
                        onClick={() => {
                            onNewConversation();
                            setDropdownVisible(false);
                        }}
                        loading={loading}
                    >
                        + New Conversation
                    </Button>
                </div>
            ),
        },
        // Existing conversations
        ...conversations.map((conv) => ({
            key: conv.id,
            label: (
                <div
                    style={{
                        padding: '8px 4px',
                        borderRadius: 4,
                        backgroundColor: currentConversation?.id === conv.id ? '#e6f7ff' : 'transparent',
                        border: currentConversation?.id === conv.id ? '1px solid #91d5ff' : '1px solid transparent'
                    }}
                    onClick={() => {
                        onConversationSelect(conv);
                        setDropdownVisible(false);
                    }}
                >
                    <div style={{ marginBottom: 4 }}>
                        <Text strong style={{ fontSize: 13 }}>
                            {conv.title || 'Untitled Conversation'}
                        </Text>
                        {currentConversation?.id === conv.id && (
                            <Tag size="small" color="blue" style={{ marginLeft: 8, fontSize: 10 }}>
                                Active
                            </Tag>
                        )}
                    </div>

                    <Space size={8} style={{ fontSize: 11, color: '#666' }}>
                        <span>
                            <MessageOutlined style={{ marginRight: 2 }} />
                            {conv.messageCount || 0}
                        </span>
                        <span>
                            <ClockCircleOutlined style={{ marginRight: 2 }} />
                            {formatTime(conv.updatedAt || conv.createdAt)}
                        </span>
                    </Space>

                    {/* Conversation mode indicator */}
                    <div style={{ marginTop: 4 }}>
                        <Tag
                            icon={<LinkOutlined />}
                            color="green"
                            size="small"
                            style={{ fontSize: 9, margin: 0 }}
                        >
                            Context-Aware
                        </Tag>
                    </div>
                </div>
            ),
        })),
    ];

    if (conversations.length === 0) {
        menuItems.push({
            key: 'empty',
            label: (
                <div style={{ padding: '8px 4px', textAlign: 'center' }}>
                    <Text type="secondary" style={{ fontSize: 12 }}>
                        No conversations yet
                    </Text>
                </div>
            ),
        });
    }

    return (
        <Dropdown
            menu={{ items: menuItems }}
            trigger={['click']}
            open={dropdownVisible}
            onOpenChange={setDropdownVisible}
            placement="bottomLeft"
            overlayStyle={{ minWidth: 280 }}
        >
            <Button
                style={{
                    width: '100%',
                    textAlign: 'left',
                    height: 'auto',
                    padding: '8px 12px'
                }}
            >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={{ display: 'flex', alignItems: 'center', marginBottom: 2 }}>
                            <SwapOutlined style={{ marginRight: 6, color: '#1890ff' }} />
                            <Text strong style={{ fontSize: 13 }}>
                                {currentConversation?.title || 'Select Conversation'}
                            </Text>
                        </div>

                        {currentConversation && (
                            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                                <Tag
                                    icon={<LinkOutlined />}
                                    color="green"
                                    size="small"
                                    style={{ fontSize: 9, margin: 0 }}
                                >
                                    Context Mode
                                </Tag>
                                <Text type="secondary" style={{ fontSize: 11 }}>
                                    {conversations.length} conversations
                                </Text>
                            </div>
                        )}
                    </div>
                    <DownOutlined style={{ fontSize: 10, color: '#8c8c8c' }} />
                </div>
            </Button>
        </Dropdown>
    );
};

export default ConversationSwitcher;