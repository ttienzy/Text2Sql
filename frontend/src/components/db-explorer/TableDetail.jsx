import { useState } from 'react';
import { Card, Descriptions, Table, Tag, Space, Button, Empty, Spin, Tabs, Modal, message, Tooltip, Progress } from 'antd';
import {
    TableOutlined, KeyOutlined, LinkOutlined, DatabaseOutlined, MessageOutlined,
    EyeOutlined, CopyOutlined, StarOutlined, StarFilled, WarningOutlined, ArrowRightOutlined,
    ThunderboltOutlined
} from '@ant-design/icons';
import { formatNumber } from '../../utils/formatters';
import { useSampleDataQuery } from '../../api/dbExplorer/queries';
import QuerySuggestions from './QuerySuggestions';

const ROLE_COLORS = {
    Master: '#1890ff',
    Transaction: '#52c41a',
    Bridge: '#faad14',
    Configuration: '#722ed1',
    LogAudit: '#8c8c8c',
};

const ROLE_ICONS = {
    Master: '🏷️',
    Transaction: '💳',
    Bridge: '🔗',
    Configuration: '⚙️',
    LogAudit: '📝',
};

const TableDetail = ({ table, loading, onQueryTable, onJumpToTable, pinnedTables = [], onTogglePin }) => {
    const [sampleModalVisible, setSampleModalVisible] = useState(false);
    const [enableSampleQuery, setEnableSampleQuery] = useState(false);

    // Sample data query
    const { data: sampleData, isLoading: sampleLoading } = useSampleDataQuery(
        table?.connectionId,
        table?.tableName,
        { enabled: enableSampleQuery }
    );

    if (loading) {
        return (
            <div style={{ textAlign: 'center', padding: '40px 0' }}>
                <Spin size="large" />
            </div>
        );
    }

    if (!table) {
        return (
            <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Select a table to view details"
                style={{ marginTop: 100 }}
            />
        );
    }

    const isPinned = pinnedTables.includes(table.tableName);

    // Handle sample data button click
    const handleViewSampleData = () => {
        setEnableSampleQuery(true);
        setSampleModalVisible(true);
    };

    // Copy to clipboard helper
    const copyToClipboard = (text, label) => {
        navigator.clipboard.writeText(text);
        message.success(`${label} copied to clipboard`);
    };

    // Generate DDL
    const generateDDL = () => {
        const columns = table.columns.map(col => {
            let def = `  [${col.columnName}] ${col.dataType}`;
            if (col.maxLength) def += `(${col.maxLength})`;
            if (!col.isNullable) def += ' NOT NULL';
            return def;
        }).join(',\n');

        const pks = table.columns.filter(c => c.isPrimaryKey).map(c => c.columnName);
        const pkConstraint = pks.length > 0 ? `,\n  PRIMARY KEY (${pks.join(', ')})` : '';

        return `CREATE TABLE [${table.schema}].[${table.tableName}] (\n${columns}${pkConstraint}\n);`;
    };

    // Generate SELECT statement
    const generateSelect = () => {
        const columns = table.columns.map(c => `  [${c.columnName}]`).join(',\n');
        return `SELECT\n${columns}\nFROM [${table.schema}].[${table.tableName}];`;
    };

    // Columns table configuration
    const columnColumns = [
        {
            title: 'Column',
            dataIndex: 'columnName',
            key: 'columnName',
            width: '30%',
            render: (text, record) => (
                <Space>
                    {record.isPrimaryKey && <Tooltip title="Primary Key"><KeyOutlined style={{ color: '#faad14' }} /></Tooltip>}
                    {record.isForeignKey && <Tooltip title="Foreign Key"><LinkOutlined style={{ color: '#1890ff' }} /></Tooltip>}
                    <span style={{ fontWeight: record.isPrimaryKey ? 500 : 'normal' }}>{text}</span>
                </Space>
            ),
        },
        {
            title: 'Type',
            dataIndex: 'dataType',
            key: 'dataType',
            width: '20%',
            render: (text, record) => (
                <span>
                    {text}
                    {record.maxLength && `(${record.maxLength})`}
                </span>
            ),
        },
        {
            title: 'Nullable',
            dataIndex: 'isNullable',
            key: 'isNullable',
            width: '15%',
            render: (nullable) => (
                <Tag color={nullable ? 'default' : 'blue'}>
                    {nullable ? 'NULL' : 'NOT NULL'}
                </Tag>
            ),
        },
        {
            title: 'Stats',
            key: 'stats',
            width: '25%',
            render: (_, record) => {
                if (!record.statistics) return '-';
                const nullRate = record.statistics.nullRate * 100;
                const isHighNull = nullRate > 80;

                return (
                    <Space direction="vertical" size={2} style={{ width: '100%' }}>
                        {record.statistics.nullRate > 0 && (
                            <div>
                                {isHighNull && <WarningOutlined style={{ color: '#ff4d4f', marginRight: 4 }} />}
                                <span style={{ fontSize: 12, color: isHighNull ? '#ff4d4f' : '#999' }}>
                                    Null: {nullRate.toFixed(1)}%
                                </span>
                                <Progress
                                    percent={nullRate}
                                    size="small"
                                    showInfo={false}
                                    strokeColor={isHighNull ? '#ff4d4f' : '#d9d9d9'}
                                    style={{ marginTop: 2 }}
                                />
                            </div>
                        )}
                        {record.statistics.distinctCount > 0 && (
                            <span style={{ fontSize: 12, color: '#999' }}>
                                Distinct: {formatNumber(record.statistics.distinctCount)}
                            </span>
                        )}
                    </Space>
                );
            },
        },
        {
            title: 'Actions',
            key: 'actions',
            width: '10%',
            render: (_, record) => (
                <Space>
                    <Tooltip title="Copy column name">
                        <Button
                            type="text"
                            size="small"
                            icon={<CopyOutlined />}
                            onClick={() => copyToClipboard(record.columnName, 'Column name')}
                        />
                    </Tooltip>
                    {record.isForeignKey && record.referencedTable && (
                        <Tooltip title={`Jump to ${record.referencedTable}`}>
                            <Button
                                type="text"
                                size="small"
                                icon={<ArrowRightOutlined />}
                                onClick={() => onJumpToTable(record.referencedTable)}
                            />
                        </Tooltip>
                    )}
                </Space>
            ),
        },
    ];

    // Relationships table configuration
    const relationshipColumns = [
        {
            title: 'Direction',
            dataIndex: 'direction',
            key: 'direction',
            render: (direction) => (
                <Tag color={direction === 'outgoing' ? 'blue' : 'green'}>
                    {direction === 'outgoing' ? 'FK →' : '← Referenced by'}
                </Tag>
            ),
        },
        {
            title: 'Related Table',
            dataIndex: 'relatedTable',
            key: 'relatedTable',
            render: (text) => (
                <Space>
                    <TableOutlined />
                    <span>{text}</span>
                </Space>
            ),
        },
        {
            title: 'Via Column',
            dataIndex: 'viaColumn',
            key: 'viaColumn',
        },
        {
            title: 'Type',
            dataIndex: 'type',
            key: 'type',
        },
        {
            title: 'Actions',
            key: 'actions',
            render: (_, record) => (
                <Button
                    type="link"
                    size="small"
                    icon={<ArrowRightOutlined />}
                    onClick={() => onJumpToTable(record.relatedTable)}
                >
                    Jump
                </Button>
            ),
        },
    ];

    // Indexes table configuration
    const indexColumns = [
        {
            title: 'Index Name',
            dataIndex: 'indexName',
            key: 'indexName',
            render: (text, record) => (
                <Space>
                    {record.isPrimaryKey && <KeyOutlined style={{ color: '#faad14' }} />}
                    <span>{text}</span>
                </Space>
            ),
        },
        {
            title: 'Columns',
            dataIndex: 'columns',
            key: 'columns',
            render: (columns) => columns.join(', '),
        },
        {
            title: 'Type',
            key: 'type',
            render: (_, record) => (
                <Space>
                    {record.isPrimaryKey && <Tag color="gold">Primary Key</Tag>}
                    {record.isUnique && <Tag color="blue">Unique</Tag>}
                    {!record.isPrimaryKey && !record.isUnique && <Tag>Index</Tag>}
                </Space>
            ),
        },
    ];

    const tabItems = [
        {
            key: 'columns',
            label: `Columns (${table.columns?.length || 0})`,
            children: (
                <Table
                    dataSource={table.columns}
                    columns={columnColumns}
                    rowKey="columnName"
                    pagination={false}
                    size="small"
                />
            ),
        },
        {
            key: 'suggestions',
            label: (
                <span>
                    <ThunderboltOutlined /> Suggestions
                </span>
            ),
            children: (
                <QuerySuggestions
                    connectionId={table.connectionId}
                    tableName={table.tableName}
                />
            ),
        },
        {
            key: 'relationships',
            label: `Relationships (${table.relationships?.length || 0})`,
            children: table.relationships?.length > 0 ? (
                <Table
                    dataSource={table.relationships}
                    columns={relationshipColumns}
                    rowKey={(record) => `${record.direction}-${record.relatedTable}-${record.viaColumn}`}
                    pagination={false}
                    size="small"
                />
            ) : (
                <Empty description="No relationships" />
            ),
        },
        {
            key: 'indexes',
            label: `Indexes (${table.indexes?.length || 0})`,
            children: table.indexes?.length > 0 ? (
                <Table
                    dataSource={table.indexes}
                    columns={indexColumns}
                    rowKey="indexName"
                    pagination={false}
                    size="small"
                />
            ) : (
                <Empty description="No indexes" />
            ),
        },
    ];

    return (
        <div style={{ height: '100%', overflow: 'auto', padding: 16 }}>
            <Card
                title={
                    <Space>
                        <span style={{ fontSize: 20 }}>📋</span>
                        <TableOutlined />
                        <span>{table.tableName}</span>
                        <Tag color={ROLE_COLORS[table.role]}>
                            {ROLE_ICONS[table.role]} {table.role}
                        </Tag>
                        {table.module && (
                            <Tag color="purple">📦 {table.module}</Tag>
                        )}
                    </Space>
                }
                extra={
                    <Space>
                        <Tooltip title={isPinned ? 'Unpin table' : 'Pin table'}>
                            <Button
                                type="text"
                                icon={isPinned ? <StarFilled style={{ color: '#faad14' }} /> : <StarOutlined />}
                                onClick={() => onTogglePin(table.tableName)}
                            />
                        </Tooltip>
                        <Button
                            icon={<EyeOutlined />}
                            onClick={handleViewSampleData}
                        >
                            Sample Data
                        </Button>
                        <Button
                            icon={<CopyOutlined />}
                            onClick={() => copyToClipboard(generateDDL(), 'DDL')}
                        >
                            Copy DDL
                        </Button>
                        <Button
                            icon={<CopyOutlined />}
                            onClick={() => copyToClipboard(generateSelect(), 'SELECT')}
                        >
                            Copy SELECT
                        </Button>
                        <Button.Group>
                            <Button
                                type="primary"
                                icon={<MessageOutlined />}
                                onClick={() => onQueryTable(table, 'query')}
                            >
                                Query
                            </Button>
                            <Button
                                type="primary"
                                icon={<MessageOutlined />}
                                onClick={() => onQueryTable(table, 'relationships')}
                            >
                                Explain Relations
                            </Button>
                            <Button
                                type="primary"
                                icon={<WarningOutlined />}
                                onClick={() => onQueryTable(table, 'quality')}
                            >
                                Check Quality
                            </Button>
                        </Button.Group>
                    </Space>
                }
            >
                <Descriptions column={2} size="small" style={{ marginBottom: 16 }}>
                    <Descriptions.Item label="Schema">{table.schema}</Descriptions.Item>
                    <Descriptions.Item label="Module">{table.module || '-'}</Descriptions.Item>
                    <Descriptions.Item label="Row Count">{formatNumber(table.rowCount)}</Descriptions.Item>
                    <Descriptions.Item label="Columns">{table.columns?.length || 0}</Descriptions.Item>
                </Descriptions>

                {table.description && (
                    <div style={{ marginBottom: 16, padding: 12, backgroundColor: '#f5f5f5', borderRadius: 4 }}>
                        <div style={{ fontWeight: 500, marginBottom: 4, color: '#666' }}>Description:</div>
                        <div style={{ color: '#666', fontStyle: 'italic' }}>"{table.description}"</div>
                    </div>
                )}

                <Tabs items={tabItems} />
            </Card>

            {/* Sample Data Modal */}
            <Modal
                title={`Sample Data - ${table.tableName}`}
                open={sampleModalVisible}
                onCancel={() => setSampleModalVisible(false)}
                width={1000}
                footer={[
                    <Button key="close" onClick={() => setSampleModalVisible(false)}>
                        Close
                    </Button>
                ]}
            >
                {sampleLoading ? (
                    <div style={{ textAlign: 'center', padding: '40px 0' }}>
                        <Spin />
                        <div style={{ marginTop: 16 }}>Loading sample data...</div>
                    </div>
                ) : sampleData ? (
                    <Table
                        dataSource={sampleData.rows}
                        columns={sampleData.columns.map(col => ({
                            title: col,
                            dataIndex: col,
                            key: col,
                            render: (value) => value === null ? <Tag>NULL</Tag> : String(value)
                        }))}
                        pagination={false}
                        size="small"
                        scroll={{ x: 'max-content' }}
                    />
                ) : (
                    <Empty description="No sample data available" />
                )}
            </Modal>
        </div>
    );
};

export default TableDetail;
