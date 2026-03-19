import { Modal, Alert, Tabs, Table, Tag, Space, Button, Empty, Spin } from 'antd';
import {
    PlusOutlined,
    MinusOutlined,
    EditOutlined,
    ReloadOutlined,
    ClockCircleOutlined,
} from '@ant-design/icons';
import dayjs from 'dayjs';

const SchemaChangesModal = ({ visible, onClose, changes, loading, onReAnalyze }) => {
    if (!changes && !loading) {
        return null;
    }

    const formatDateTime = (date) => {
        return dayjs(date).format('YYYY-MM-DD HH:mm:ss');
    };

    // New tables columns
    const newTablesColumns = [
        {
            title: 'Table Name',
            dataIndex: 'tableName',
            key: 'tableName',
            render: (text) => (
                <Space>
                    <PlusOutlined style={{ color: '#52c41a' }} />
                    <span style={{ fontWeight: 500 }}>{text}</span>
                </Space>
            ),
        },
        {
            title: 'Schema',
            dataIndex: 'schema',
            key: 'schema',
        },
        {
            title: 'Description',
            dataIndex: 'description',
            key: 'description',
            render: (text) => text || '-',
        },
    ];

    // Deleted tables columns
    const deletedTablesColumns = [
        {
            title: 'Table Name',
            dataIndex: 'tableName',
            key: 'tableName',
            render: (text) => (
                <Space>
                    <MinusOutlined style={{ color: '#ff4d4f' }} />
                    <span style={{ fontWeight: 500 }}>{text}</span>
                </Space>
            ),
        },
        {
            title: 'Schema',
            dataIndex: 'schema',
            key: 'schema',
        },
        {
            title: 'Description',
            dataIndex: 'description',
            key: 'description',
            render: (text) => text || '-',
        },
    ];

    // Modified tables columns
    const modifiedTablesColumns = [
        {
            title: 'Table Name',
            dataIndex: 'tableName',
            key: 'tableName',
            render: (text) => (
                <Space>
                    <EditOutlined style={{ color: '#faad14' }} />
                    <span style={{ fontWeight: 500 }}>{text}</span>
                </Space>
            ),
        },
        {
            title: 'Changes',
            key: 'changes',
            render: (_, record) => (
                <Space direction="vertical" size={4}>
                    {record.columnChanges?.length > 0 && (
                        <Tag color="blue">
                            {record.columnChanges.length} column change(s)
                        </Tag>
                    )}
                    {record.indexChanges?.length > 0 && (
                        <Tag color="purple">
                            {record.indexChanges.length} index change(s)
                        </Tag>
                    )}
                </Space>
            ),
        },
    ];

    // Column changes expandable
    const columnChangesColumns = [
        {
            title: 'Column',
            dataIndex: 'columnName',
            key: 'columnName',
        },
        {
            title: 'Change Type',
            dataIndex: 'type',
            key: 'type',
            render: (type) => {
                const colors = {
                    added: 'green',
                    removed: 'red',
                    modified: 'orange',
                };
                return <Tag color={colors[type]}>{type.toUpperCase()}</Tag>;
            },
        },
        {
            title: 'Old Type',
            dataIndex: 'oldDataType',
            key: 'oldDataType',
            render: (text) => text || '-',
        },
        {
            title: 'New Type',
            dataIndex: 'newDataType',
            key: 'newDataType',
            render: (text) => text || '-',
        },
        {
            title: 'Nullable',
            key: 'nullable',
            render: (_, record) => {
                if (record.type === 'added') {
                    return record.newIsNullable ? 'NULL' : 'NOT NULL';
                }
                if (record.type === 'removed') {
                    return record.oldIsNullable ? 'NULL' : 'NOT NULL';
                }
                if (record.oldIsNullable !== record.newIsNullable) {
                    return (
                        <span>
                            {record.oldIsNullable ? 'NULL' : 'NOT NULL'} →{' '}
                            {record.newIsNullable ? 'NULL' : 'NOT NULL'}
                        </span>
                    );
                }
                return record.newIsNullable ? 'NULL' : 'NOT NULL';
            },
        },
    ];

    const expandedRowRender = (record) => {
        return (
            <div style={{ padding: '8px 16px' }}>
                {record.columnChanges?.length > 0 && (
                    <div style={{ marginBottom: 16 }}>
                        <div style={{ fontWeight: 500, marginBottom: 8 }}>Column Changes:</div>
                        <Table
                            dataSource={record.columnChanges}
                            columns={columnChangesColumns}
                            pagination={false}
                            size="small"
                            rowKey="columnName"
                        />
                    </div>
                )}
                {record.indexChanges?.length > 0 && (
                    <div>
                        <div style={{ fontWeight: 500, marginBottom: 8 }}>Index Changes:</div>
                        <Table
                            dataSource={record.indexChanges}
                            columns={[
                                {
                                    title: 'Index Name',
                                    dataIndex: 'indexName',
                                    key: 'indexName',
                                },
                                {
                                    title: 'Type',
                                    dataIndex: 'type',
                                    key: 'type',
                                    render: (type) => {
                                        const colors = {
                                            added: 'green',
                                            removed: 'red',
                                        };
                                        return <Tag color={colors[type]}>{type.toUpperCase()}</Tag>;
                                    },
                                },
                                {
                                    title: 'Columns',
                                    dataIndex: 'columns',
                                    key: 'columns',
                                    render: (cols) => cols.join(', '),
                                },
                            ]}
                            pagination={false}
                            size="small"
                            rowKey="indexName"
                        />
                    </div>
                )}
            </div>
        );
    };

    const tabItems = [
        {
            key: 'new',
            label: (
                <span>
                    <PlusOutlined style={{ color: '#52c41a' }} />
                    New Tables ({changes?.newTables?.length || 0})
                </span>
            ),
            children: changes?.newTables?.length > 0 ? (
                <Table
                    dataSource={changes.newTables}
                    columns={newTablesColumns}
                    pagination={false}
                    size="small"
                    rowKey="tableName"
                />
            ) : (
                <Empty description="No new tables" />
            ),
        },
        {
            key: 'deleted',
            label: (
                <span>
                    <MinusOutlined style={{ color: '#ff4d4f' }} />
                    Deleted Tables ({changes?.deletedTables?.length || 0})
                </span>
            ),
            children: changes?.deletedTables?.length > 0 ? (
                <Table
                    dataSource={changes.deletedTables}
                    columns={deletedTablesColumns}
                    pagination={false}
                    size="small"
                    rowKey="tableName"
                />
            ) : (
                <Empty description="No deleted tables" />
            ),
        },
        {
            key: 'modified',
            label: (
                <span>
                    <EditOutlined style={{ color: '#faad14' }} />
                    Modified Tables ({changes?.modifiedTables?.length || 0})
                </span>
            ),
            children: changes?.modifiedTables?.length > 0 ? (
                <Table
                    dataSource={changes.modifiedTables}
                    columns={modifiedTablesColumns}
                    expandable={{
                        expandedRowRender,
                        rowExpandable: (record) =>
                            (record.columnChanges?.length > 0) || (record.indexChanges?.length > 0),
                    }}
                    pagination={false}
                    size="small"
                    rowKey="tableName"
                />
            ) : (
                <Empty description="No modified tables" />
            ),
        },
    ];

    return (
        <Modal
            title={
                <Space>
                    <ClockCircleOutlined />
                    <span>Schema Changes Detected</span>
                </Space>
            }
            open={visible}
            onCancel={onClose}
            width={900}
            footer={[
                <Button key="close" onClick={onClose}>
                    Close
                </Button>,
                <Button
                    key="reanalyze"
                    type="primary"
                    icon={<ReloadOutlined />}
                    onClick={onReAnalyze}
                >
                    Re-analyze Database
                </Button>,
            ]}
        >
            {loading ? (
                <div style={{ textAlign: 'center', padding: '40px 0' }}>
                    <Spin />
                    <div style={{ marginTop: 16 }}>Detecting schema changes...</div>
                </div>
            ) : changes?.hasChanges ? (
                <>
                    <Alert
                        message="Schema Changes Detected"
                        description={
                            <div>
                                <div>
                                    The database schema has changed since the last analysis.
                                    Review the changes below and re-analyze to update the cache.
                                </div>
                                {changes.comparedAt && (
                                    <div style={{ marginTop: 8, fontSize: 12, color: '#666' }}>
                                        Compared at: {formatDateTime(changes.comparedAt)}
                                    </div>
                                )}
                            </div>
                        }
                        type="warning"
                        showIcon
                        style={{ marginBottom: 16 }}
                    />
                    <Tabs items={tabItems} />
                </>
            ) : (
                <Alert
                    message="No Changes Detected"
                    description="The database schema matches the cached version. No changes found."
                    type="success"
                    showIcon
                />
            )}
        </Modal>
    );
};

export default SchemaChangesModal;
