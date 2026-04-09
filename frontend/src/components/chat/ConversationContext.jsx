import { useState } from 'react';
import { Card, Statistic, Tag, Button, Collapse, Space, Typography, Tooltip } from 'antd';
import {
    MessageOutlined,
    ClockCircleOutlined,
    DollarOutlined,
    TagsOutlined,
    BarChartOutlined,
    EyeOutlined,
    EyeInvisibleOutlined
} from '@ant-design/icons';
import { useConversationContextQuery } from '../../api/agent/v2';

const { Panel } = Collapse;
const { Text } = Typography;

/**
 * ConversationContext - Hiển thị thông tin chi tiết về cuộc hội thoại
 * Bao gồm: số lượng tin nhắn, token usage, cost, topics, v.v.
 */
const ConversationContext = ({ conversationId, visible = true }) => {
    const [isExpanded, setIsExpanded] = useState(false);

    const {
        data: contextData,
        isLoading,
        error,
        refetch
    } = useConversationContextQuery(conversationId, {
        enabled: !!conversationId && visible,
    });

    if (!visible || !conversationId) {
        return null;
    }

    if (error) {
        return (
            <Card size="small" title="Conversation Context" style={{ marginBottom: 16 }}>
                <Text type="danger">Failed to load conversation context</Text>
                <Button size="small" onClick={() => refetch()} style={{ marginLeft: 8 }}>
                    Retry
                </Button>
            </Card>
        );
    }

    if (isLoading) {
        return (
            <Card size="small" title="Conversation Context" loading style={{ marginBottom: 16 }}>
                <div style={{ height: 100 }} />
            </Card>
        );
    }

    const formatCurrency = (amount) => {
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD',
            minimumFractionDigits: 4,
        }).format(amount);
    };

    const formatDate = (dateString) => {
        return new Date(dateString).toLocaleString('vi-VN');
    };

    return (
        <Card
            size="small"
            title={
                <Space>
                    <BarChartOutlined />
                    Conversation Analytics
                    <Tooltip title={isExpanded ? "Collapse" : "Expand"}>
                        <Button
                            type="text"
                            size="small"
                            icon={isExpanded ? <EyeInvisibleOutlined /> : <EyeOutlined />}
                            onClick={() => setIsExpanded(!isExpanded)}
                        />
                    </Tooltip>
                </Space>
            }
            style={{ marginBottom: 16 }}
        >
            {/* Basic Stats - Always visible */}
            <Space direction="vertical" style={{ width: '100%' }} size="small">
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <Statistic
                        title="Messages"
                        value={contextData?.messageCount || 0}
                        prefix={<MessageOutlined />}
                        valueStyle={{ fontSize: 14 }}
                    />
                    <Statistic
                        title="Turns"
                        value={contextData?.turnCount || 0}
                        prefix={<ClockCircleOutlined />}
                        valueStyle={{ fontSize: 14 }}
                    />
                </div>

                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <Statistic
                        title="Tokens"
                        value={contextData?.totalTokensUsed || 0}
                        valueStyle={{ fontSize: 14 }}
                    />
                    <Statistic
                        title="Cost"
                        value={formatCurrency(contextData?.totalCost || 0)}
                        prefix={<DollarOutlined />}
                        valueStyle={{ fontSize: 14 }}
                    />
                </div>

                {/* Expanded Details */}
                {isExpanded && (
                    <Collapse ghost size="small">
                        <Panel header="Detailed Information" key="details">
                            <Space direction="vertical" style={{ width: '100%' }} size="small">

                                {/* Timestamps */}
                                <div>
                                    <Text strong>Started:</Text>
                                    <br />
                                    <Text type="secondary" style={{ fontSize: 12 }}>
                                        {contextData?.startedAt ? formatDate(contextData.startedAt) : 'N/A'}
                                    </Text>
                                </div>

                                <div>
                                    <Text strong>Last Active:</Text>
                                    <br />
                                    <Text type="secondary" style={{ fontSize: 12 }}>
                                        {contextData?.lastActiveAt ? formatDate(contextData.lastActiveAt) : 'N/A'}
                                    </Text>
                                </div>

                                {/* Query Statistics */}
                                <div>
                                    <Text strong>Queries Executed:</Text>
                                    <br />
                                    <Text>{contextData?.queriesExecuted || 0}</Text>
                                </div>

                                {/* Topics */}
                                {contextData?.topics && contextData.topics.length > 0 && (
                                    <div>
                                        <Text strong>
                                            <TagsOutlined /> Topics:
                                        </Text>
                                        <br />
                                        <div style={{ marginTop: 4 }}>
                                            {contextData.topics.slice(0, 5).map((topic, index) => (
                                                <Tag key={index} size="small" style={{ marginBottom: 4 }}>
                                                    {topic}
                                                </Tag>
                                            ))}
                                            {contextData.topics.length > 5 && (
                                                <Tag size="small" color="default">
                                                    +{contextData.topics.length - 5} more
                                                </Tag>
                                            )}
                                        </div>
                                    </div>
                                )}

                                {/* Conversation ID for debugging */}
                                <div>
                                    <Text strong>ID:</Text>
                                    <br />
                                    <Text code style={{ fontSize: 10 }}>
                                        {conversationId}
                                    </Text>
                                </div>
                            </Space>
                        </Panel>
                    </Collapse>
                )}
            </Space>
        </Card>
    );
};

export default ConversationContext;