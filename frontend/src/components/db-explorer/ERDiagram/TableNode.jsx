import { memo } from 'react';
import { Handle, Position } from 'reactflow';
import { Card, Tag, Space } from 'antd';
import { TableOutlined, KeyOutlined, LinkOutlined } from '@ant-design/icons';

const ROLE_COLORS = {
    master: '#1677ff',
    transaction: '#389e0d',
    bridge: '#d48806',
    configuration: '#722ed1',
    logaudit: '#595959',
    unknown: '#8c8c8c',
};

const TableNode = ({ data, selected }) => {
    const {
        tableName,
        role = 'unknown',
        rowCount,
        columnCount,
        module,
        columns = [],
        showColumns = true,
        isFocused,
        isRelated,
        dimmed,
    } = data;

    const displayColumns = columns.slice(0, 10);
    const hasMoreColumns = columns.length > 10;
    const borderColor = selected || isFocused
        ? ROLE_COLORS[role]
        : isRelated
            ? '#13c2c2'
            : '#d9d9d9';

    return (
        <div style={{ minWidth: 240, maxWidth: 320, opacity: dimmed ? 0.35 : 1 }}>
            <Handle type="target" position={Position.Top} />

            <Card
                size="small"
                style={{
                    border: selected || isFocused || isRelated ? `2px solid ${borderColor}` : '1px solid #d9d9d9',
                    borderRadius: 6,
                    boxShadow: selected || isFocused ? '0 6px 16px rgba(0,0,0,0.16)' : '0 2px 8px rgba(0,0,0,0.08)',
                    cursor: 'pointer',
                    transition: 'border 0.2s, box-shadow 0.2s, opacity 0.2s',
                }}
                bodyStyle={{ padding: 0 }}
            >
                <div style={{ padding: 12, background: ROLE_COLORS[role] || ROLE_COLORS.unknown, color: '#fff' }}>
                    <Space style={{ width: '100%', justifyContent: 'space-between' }}>
                        <Space>
                            <TableOutlined style={{ fontSize: 14 }} />
                            <span style={{ fontWeight: 600, fontSize: 13, maxWidth: 190, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                {tableName}
                            </span>
                        </Space>
                    </Space>

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
                                {module}
                            </Tag>
                        )}
                    </div>
                </div>

                {showColumns && (
                    <div style={{ padding: '8px 0', maxHeight: 210, overflowY: 'auto' }}>
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
                                <div style={{ width: 16, display: 'flex', justifyContent: 'center' }}>
                                    {column.isPrimaryKey && (
                                        <KeyOutlined style={{ color: '#d48806', fontSize: 10 }} />
                                    )}
                                    {column.isForeignKey && !column.isPrimaryKey && (
                                        <LinkOutlined style={{ color: '#1677ff', fontSize: 10 }} />
                                    )}
                                </div>

                                <span
                                    style={{
                                        flex: 1,
                                        minWidth: 0,
                                        fontWeight: column.isPrimaryKey ? 600 : 400,
                                        color: column.isPrimaryKey ? '#000' : '#333',
                                        overflow: 'hidden',
                                        textOverflow: 'ellipsis',
                                        whiteSpace: 'nowrap',
                                    }}
                                >
                                    {column.name}
                                </span>

                                <span style={{ color: '#999', fontSize: 10, maxWidth: 92, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                                    {column.type}
                                </span>

                                {column.isNullable && (
                                    <span style={{ color: '#999', fontSize: 9 }}>?</span>
                                )}
                            </div>
                        ))}

                        {hasMoreColumns && (
                            <div style={{
                                padding: '4px 12px',
                                fontSize: 10,
                                color: '#666',
                                textAlign: 'center'
                            }}>
                                +{columns.length - 10} more columns
                            </div>
                        )}
                    </div>
                )}

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
