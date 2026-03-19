import React from 'react';
import { Tag, Tooltip, Space } from 'antd';
import { LinkOutlined, DatabaseOutlined } from '@ant-design/icons';

/**
 * ConversationContextIndicator - Shows which entities are in conversation context
 * Displays when AI is using context from previous messages to understand pronouns
 */
const ConversationContextIndicator = ({
    contextEntities = [],
    primaryEntity = null,
    show = false
}) => {
    if (!show || (!contextEntities.length && !primaryEntity)) {
        return null;
    }

    const entities = primaryEntity
        ? [primaryEntity, ...contextEntities.filter(e => e !== primaryEntity)]
        : contextEntities;

    const uniqueEntities = [...new Set(entities)];

    if (uniqueEntities.length === 0) {
        return null;
    }

    return (
        <div style={{
            padding: '6px 12px',
            backgroundColor: '#e6f7ff',
            borderLeft: '3px solid #1890ff',
            borderRadius: 4,
            marginBottom: 8,
        }}>
            <Space size={8} wrap>
                <Tooltip title="AI is using context from previous messages to understand your question">
                    <Space size={4}>
                        <LinkOutlined style={{ color: '#1890ff', fontSize: 12 }} />
                        <span style={{ fontSize: 12, color: '#096dd9', fontWeight: 500 }}>
                            Using context:
                        </span>
                    </Space>
                </Tooltip>

                {uniqueEntities.map((entity, index) => (
                    <Tag
                        key={index}
                        icon={<DatabaseOutlined />}
                        color={index === 0 ? 'blue' : 'default'}
                        style={{
                            fontSize: 11,
                            margin: 0,
                            fontWeight: index === 0 ? 500 : 400
                        }}
                    >
                        {entity}
                        {index === 0 && primaryEntity && ' (primary)'}
                    </Tag>
                ))}
            </Space>
        </div>
    );
};

export default ConversationContextIndicator;
