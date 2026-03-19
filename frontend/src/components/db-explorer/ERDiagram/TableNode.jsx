import { memo } from 'react';
import { Handle, Position } from 'reactflow';
import { Card, Tag, Space, Divider } from 'antd';
import { TableOutlined, KeyOutlined, LinkOutlined } from '@ant-design/icons';

const ROLE_COLORS = {
    master: '#1890ff',
    transaction: '#52c41a',
    bridge: '#faad14',
    configuration: '#722ed1',
    logaudit: '#8c8c8c',
    unknown: '#d9d9d9',
};

const TableNode = ({ data, selected }) => {
    const { tableName, role, rowCount, columnCount, module, columns = [] } = data;

    // Limit columns to show (max 10)
    const displayColumns = columns.slice(0, 10);
    const hasMoreColumns = columns.length > 10;

    return (
        <div style={{ minWidth: 220, maxWidth: 300 }}>
            <Handle type="target" position={Position.Top} />

            <Card
                size="small"
                style={{
                    border: selected ? `2px solid ${ROLE_COLORS[role]}` : '1px solid #d9d9d9',
                    borderRadius: 8,
                    boxShadow: selected ? '0 4px 12px rgba(0,0,0,0.15)' : '0 2px 8px rgba(0,0,0,0.1)',
                    cursor: 'pointer',
                    transition: 'all 0.3s',
                }}
                bodyStyle={{ padding: 0 }}
            >
                {/* Header */}
                <div style={{ padding: 12, background: ROLE_COLORS[role], color: '#fff' }}>
                    <Space style={{ width: '100%', justifyContent: 'space-between' }}>
                        <Space>
                            <TableOutlined style={{ fontSize: 14 }} />
                            <span style={{ fontWeight: 600, fontSize: 13 }}>
                                {tableName}
                            </span>
                        </Space>
                    </Space>

                    {/* Role & Module */}
                    <div style={{ marginTop: 4, display: 'flex', gap: 4, flexWrap: 'wrap' }}>
                        <Tag
                            style={{
                                fontSize: 10,
                                padding: '0 4px',
                                margin: 0,
                                background: 'rgba(255,255,255,0.2)',
                                border: 'none',
                                color: '#fff'
                            }}
                        >
                            {role.charAt(0).toUpperCase() + role.slice(1)}
                        </Tag>
                        {module && (
                            <Tag
                                style={{
                                    fontSize: 10,
                                    padding: '0 4px',
                                    margin: 0,
                                    background: 'rgba(255,255,255,0.2)',
                                    border: 'none',
                                    color: '#fff'
                                }}
                            >
                                📦 {module}
                            </Tag>
                        )}
                    </div>
                </div>

                {/* Columns List */}
                <div style={{ padding: '8px 0', maxHeight: 200, overflowY: 'auto' }}>
                    {displayColumns.map((column, index) => (
                        <div
                            key={column.name}
                            style={{
                                padding: '4px 12px',
                                fontSize: 11,
                                display: 'flex',
                                alignItems: 'center',
                                gap: 6,
                                borderBottom: index < displayColumns.length - 1 ? '1px solid #f0f0f0' : 'none',
                                background: column.isPrimaryKey ? '#fff7e6' : 'transparent',
                            }}
                        >
                            {/* Icons */}
                            <div style={{ width: 16, display: 'flex', justifyContent: 'center' }}>
                                {column.isPrimaryKey && (
                                    <KeyOutlined style={{ color: '#faad14', fontSize: 10 }} />
                                )}
                                {column.isForeignKey && !column.isPrimaryKey && (
                                    <LinkOutlined style={{ color: '#1890ff', fontSize: 10 }} />
                                )}
                            </div>

                            {/* Column Name */}
                            <span
                                style={{
                                    flex: 1,
                                    fontWeight: column.isPrimaryKey ? 600 : 400,
                                    color: column.isPrimaryKey ? '#000' : '#333'
                                }}
                            >
                                {column.name}
                            </span>

                            {/* Type */}
                            <span style={{ color: '#999', fontSize: 10 }}>
                                {column.type}
                            </span>

                            {/* Nullable indicator */}
                            {column.isNullable && (
                                <span style={{ color: '#999', fontSize: 9 }}>?</span>
                            )}
                        </div>
                    ))}

                    {hasMoreColumns && (
                        <div style={{
                            padding: '4px 12px',
                            fontSize: 10,
                            color: '#999',
                            textAlign: 'center',
                            fontStyle: 'italic'
                        }}>
                            +{columns.length - 10} more columns...
                        </div>
                    )}
                </div>

                {/* Footer Stats */}
                <div style={{
                    padding: '6px 12px',
                    background: '#fafafa',
                    borderTop: '1px solid #f0f0f0',
                    fontSize: 10,
                    color: '#666'
                }}>
                    <Space split={<span>·</span>} size={4}>
                        <span>{rowCount?.toLocaleString()} rows</span>
                        <span>{columnCount} columns</span>
                    </Space>
                </div>
            </Card>

            <Handle type="source" position={Position.Bottom} />
        </div>
    );
};

export default memo(TableNode);
