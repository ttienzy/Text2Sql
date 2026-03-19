import { useState } from 'react';
import { Card, List, Tag, Space, Button, Empty, Spin, message, Segmented, Tooltip } from 'antd';
import {
    ThunderboltOutlined,
    CopyOutlined,
    SendOutlined,
    BulbOutlined,
    BarChartOutlined,
    SafetyOutlined,
    LinkOutlined,
} from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { useQuerySuggestionsQuery } from '../../api/dbExplorer/queries';

const CATEGORY_CONFIG = {
    basic: {
        label: 'Basic',
        icon: <BulbOutlined />,
        color: 'blue',
    },
    analytics: {
        label: 'Analytics',
        icon: <BarChartOutlined />,
        color: 'purple',
    },
    quality: {
        label: 'Data Quality',
        icon: <SafetyOutlined />,
        color: 'orange',
    },
    relationships: {
        label: 'Relationships',
        icon: <LinkOutlined />,
        color: 'green',
    },
};

const COMPLEXITY_COLORS = {
    low: 'success',
    medium: 'warning',
    high: 'error',
};

const QuerySuggestions = ({ connectionId, tableName }) => {
    const navigate = useNavigate();
    const [categoryFilter, setCategoryFilter] = useState('all');

    const { data, isLoading, error } = useQuerySuggestionsQuery(connectionId, tableName, {
        enabled: !!connectionId && !!tableName,
    });

    const suggestions = data?.suggestions || [];

    // Filter by category
    const filteredSuggestions = categoryFilter === 'all'
        ? suggestions
        : suggestions.filter(s => s.category === categoryFilter);

    const handleCopyQuery = (query) => {
        navigator.clipboard.writeText(query);
        message.success('Query copied to clipboard!');
    };

    const handleSendToChat = (suggestion) => {
        navigate('/chat', {
            state: {
                contextTable: tableName,
                contextMessage: `Execute this query: ${suggestion.title}\n\n${suggestion.query}`,
                prefilledQuery: suggestion.query,
            },
        });
    };

    if (isLoading) {
        return (
            <div style={{ textAlign: 'center', padding: '40px 0' }}>
                <Spin size="large" tip="Generating smart query suggestions..." />
            </div>
        );
    }

    if (error) {
        return (
            <Card>
                <Empty
                    description="Failed to generate suggestions"
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                />
            </Card>
        );
    }

    if (suggestions.length === 0) {
        return (
            <Card>
                <Empty
                    description="No suggestions available"
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                />
            </Card>
        );
    }

    // Get category counts
    const categoryCounts = suggestions.reduce((acc, s) => {
        acc[s.category] = (acc[s.category] || 0) + 1;
        return acc;
    }, {});

    return (
        <div style={{ padding: 16 }}>
            {/* Header */}
            <div style={{ marginBottom: 16 }}>
                <Space direction="vertical" style={{ width: '100%' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <ThunderboltOutlined style={{ fontSize: 20, color: '#1890ff' }} />
                        <span style={{ fontSize: 16, fontWeight: 600 }}>
                            Smart Query Suggestions
                        </span>
                        <Tag color="blue">{suggestions.length} queries</Tag>
                    </div>
                    <div style={{ color: '#666', fontSize: 13 }}>
                        AI-generated queries based on table structure and relationships
                    </div>
                </Space>
            </div>

            {/* Category Filter */}
            <Segmented
                value={categoryFilter}
                onChange={setCategoryFilter}
                style={{ marginBottom: 16 }}
                options={[
                    { label: `All (${suggestions.length})`, value: 'all' },
                    ...Object.entries(CATEGORY_CONFIG).map(([key, config]) => ({
                        label: `${config.label} (${categoryCounts[key] || 0})`,
                        value: key,
                    })),
                ]}
                block
            />

            {/* Suggestions List */}
            <List
                dataSource={filteredSuggestions}
                renderItem={(suggestion) => {
                    const categoryConfig = CATEGORY_CONFIG[suggestion.category] || CATEGORY_CONFIG.basic;

                    return (
                        <List.Item
                            key={suggestion.title}
                            style={{
                                border: '1px solid #f0f0f0',
                                borderRadius: 8,
                                marginBottom: 12,
                                padding: 16,
                                background: '#fafafa',
                            }}
                        >
                            <div style={{ width: '100%' }}>
                                {/* Header */}
                                <div style={{ marginBottom: 12 }}>
                                    <Space style={{ width: '100%', justifyContent: 'space-between' }}>
                                        <Space>
                                            {categoryConfig.icon}
                                            <span style={{ fontWeight: 600, fontSize: 14 }}>
                                                {suggestion.title}
                                            </span>
                                        </Space>
                                        <Space>
                                            <Tag color={categoryConfig.color}>
                                                {categoryConfig.label}
                                            </Tag>
                                            <Tag color={COMPLEXITY_COLORS[suggestion.complexity]}>
                                                {suggestion.complexity}
                                            </Tag>
                                        </Space>
                                    </Space>
                                </div>

                                {/* Description */}
                                <div style={{ marginBottom: 12, color: '#666', fontSize: 13 }}>
                                    {suggestion.description}
                                </div>

                                {/* Query */}
                                <div
                                    style={{
                                        background: '#fff',
                                        border: '1px solid #d9d9d9',
                                        borderRadius: 4,
                                        padding: 12,
                                        marginBottom: 12,
                                        fontFamily: 'monospace',
                                        fontSize: 12,
                                        whiteSpace: 'pre-wrap',
                                        wordBreak: 'break-all',
                                    }}
                                >
                                    {suggestion.query}
                                </div>

                                {/* Actions */}
                                <Space>
                                    <Tooltip title="Copy to clipboard">
                                        <Button
                                            size="small"
                                            icon={<CopyOutlined />}
                                            onClick={() => handleCopyQuery(suggestion.query)}
                                        >
                                            Copy
                                        </Button>
                                    </Tooltip>
                                    <Tooltip title="Send to Chat and execute">
                                        <Button
                                            size="small"
                                            type="primary"
                                            icon={<SendOutlined />}
                                            onClick={() => handleSendToChat(suggestion)}
                                        >
                                            Execute in Chat
                                        </Button>
                                    </Tooltip>
                                </Space>
                            </div>
                        </List.Item>
                    );
                }}
            />
        </div>
    );
};

export default QuerySuggestions;
