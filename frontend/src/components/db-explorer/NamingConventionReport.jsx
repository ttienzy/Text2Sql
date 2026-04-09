import { useState } from 'react';
import { Card, Table, Tag, Space, Statistic, Row, Col, Button, Empty, Spin, Alert, Modal, Input, Tooltip, message, Progress } from 'antd';
import { CheckCircleOutlined, WarningOutlined, InfoCircleOutlined, CopyOutlined, EyeOutlined, FileTextOutlined } from '@ant-design/icons';
import { useNamingAnalysisQuery } from '../../api/dbExplorer/queries';

const { TextArea } = Input;

const NamingConventionReport = ({ connectionId, visible, onClose }) => {
    const [sqlModalVisible, setSqlModalVisible] = useState(false);
    const [selectedSql, setSelectedSql] = useState('');
    const [selectedTitle, setSelectedTitle] = useState('');

    const { data: report, isLoading, error } = useNamingAnalysisQuery(
        connectionId,
        { enabled: visible }
    );

    const copyToClipboard = (text, label = 'Content') => {
        navigator.clipboard.writeText(text);
        message.success(`${label} copied to clipboard`);
    };

    const showSqlModal = (sql, title) => {
        setSelectedSql(sql);
        setSelectedTitle(title);
        setSqlModalVisible(true);
    };

    const getSeverityColor = (severity) => {
        const colors = {
            'Info': 'blue',
            'Warning': 'orange',
            'Critical': 'red',
        };
        return colors[severity] || 'default';
    };

    const getPriorityColor = (priority) => {
        const colors = {
            'Low': 'default',
            'Medium': 'orange',
            'High': 'red',
        };
        return colors[priority] || 'default';
    };

    const getTypeIcon = (type) => {
        const icons = {
            'TableNaming': <FileTextOutlined />,
            'ColumnNaming': <InfoCircleOutlined />,
            'SimilarNames': <WarningOutlined />,
        };
        return icons[type] || <InfoCircleOutlined />;
    };

    const inconsistencyColumns = [
        {
            title: 'Type',
            dataIndex: 'type',
            key: 'type',
            width: '12%',
            render: (type) => (
                <Tag icon={getTypeIcon(type)} color="blue">
                    {type === 'TableNaming' ? 'Table' : type === 'ColumnNaming' ? 'Column' : 'Similar'}
                </Tag>
            ),
        },
        {
            title: 'Object',
            key: 'object',
            width: '20%',
            render: (_, record) => (
                <span style={{ fontFamily: 'monospace', fontSize: 12 }}>
                    {record.column ? `${record.table}.${record.column}` : record.table}
                </span>
            ),
        },
        {
            title: 'Current',
            dataIndex: 'currentName',
            key: 'currentName',
            width: '15%',
            render: (text) => <span style={{ fontFamily: 'monospace' }}>{text}</span>,
        },
        {
            title: 'Pattern',
            dataIndex: 'currentPattern',
            key: 'currentPattern',
            width: '12%',
            render: (pattern) => <Tag>{pattern}</Tag>,
        },
        {
            title: 'Suggested',
            dataIndex: 'suggestedName',
            key: 'suggestedName',
            width: '15%',
            render: (text) => (
                <span style={{ fontFamily: 'monospace', color: '#52c41a', fontWeight: 500 }}>
                    {text}
                </span>
            ),
        },
        {
            title: 'Severity',
            dataIndex: 'severity',
            key: 'severity',
            width: '10%',
            render: (severity) => (
                <Tag color={getSeverityColor(severity)}>
                    {severity}
                </Tag>
            ),
        },
        {
            title: 'Description',
            dataIndex: 'description',
            key: 'description',
            ellipsis: true,
        },
    ];

    const recommendationColumns = [
        {
            title: 'Priority',
            dataIndex: 'priority',
            key: 'priority',
            width: '10%',
            render: (priority) => (
                <Tag color={getPriorityColor(priority)}>
                    {priority.toUpperCase()}
                </Tag>
            ),
        },
        {
            title: 'Title',
            dataIndex: 'title',
            key: 'title',
            width: '30%',
            render: (text) => <strong>{text}</strong>,
        },
        {
            title: 'Description',
            dataIndex: 'description',
            key: 'description',
            width: '35%',
        },
        {
            title: 'Affected Tables',
            dataIndex: 'affectedTables',
            key: 'affectedTables',
            width: '15%',
            render: (tables) => (
                <span style={{ fontSize: 12, color: '#666' }}>
                    {tables.length} table{tables.length !== 1 ? 's' : ''}
                </span>
            ),
        },
        {
            title: 'Actions',
            key: 'actions',
            width: '10%',
            render: (_, record) => (
                <Space>
                    <Tooltip title="View SQL">
                        <Button
                            type="text"
                            size="small"
                            icon={<EyeOutlined />}
                            onClick={() => showSqlModal(record.sqlScript, record.title)}
                        />
                    </Tooltip>
                    <Tooltip title="Copy SQL">
                        <Button
                            type="text"
                            size="small"
                            icon={<CopyOutlined />}
                            onClick={() => copyToClipboard(record.sqlScript, 'SQL script')}
                        />
                    </Tooltip>
                </Space>
            ),
        },
    ];

    const calculatePercentage = (count, total) => {
        return total > 0 ? Math.round((count / total) * 100) : 0;
    };

    return (
        <>
            <Modal
                title={
                    <Space>
                        <FileTextOutlined style={{ color: '#1890ff' }} />
                        <span>Naming Convention Analysis</span>
                    </Space>
                }
                open={visible}
                onCancel={onClose}
                width={1400}
                footer={[
                    <Button key="close" onClick={onClose}>
                        Close
                    </Button>,
                ]}
                style={{ top: 20 }}
            >
                {isLoading ? (
                    <div style={{ textAlign: 'center', padding: '40px 0' }}>
                        <Spin size="large" />
                        <div style={{ marginTop: 16 }}>Analyzing naming conventions...</div>
                    </div>
                ) : error ? (
                    <Alert
                        message="Failed to load naming convention analysis"
                        description={error.response?.data?.details || error.message}
                        type="error"
                        showIcon
                    />
                ) : report ? (
                    <div>
                        {/* Summary Statistics */}
                        <Row gutter={16} style={{ marginBottom: 24 }}>
                            <Col span={6}>
                                <Card size="small">
                                    <Statistic
                                        title="Total Tables"
                                        value={report.totalTables}
                                        prefix={<FileTextOutlined />}
                                    />
                                </Card>
                            </Col>
                            <Col span={6}>
                                <Card size="small">
                                    <Statistic
                                        title="Total Columns"
                                        value={report.totalColumns}
                                        prefix={<InfoCircleOutlined />}
                                    />
                                </Card>
                            </Col>
                            <Col span={6}>
                                <Card size="small">
                                    <Statistic
                                        title="Inconsistencies"
                                        value={report.inconsistencies?.length || 0}
                                        valueStyle={{
                                            color: report.inconsistencies?.length > 0 ? '#ff4d4f' : '#52c41a'
                                        }}
                                        prefix={<WarningOutlined />}
                                    />
                                </Card>
                            </Col>
                            <Col span={6}>
                                <Card size="small">
                                    <Statistic
                                        title="Recommendations"
                                        value={report.recommendations?.length || 0}
                                        valueStyle={{ color: '#1890ff' }}
                                        prefix={<CheckCircleOutlined />}
                                    />
                                </Card>
                            </Col>
                        </Row>

                        {/* Dominant Patterns */}
                        <Row gutter={16} style={{ marginBottom: 24 }}>
                            <Col span={12}>
                                <Card
                                    title="Table Naming Pattern"
                                    size="small"
                                    extra={
                                        <Tag color="green" icon={<CheckCircleOutlined />}>
                                            {report.dominantTablePattern}
                                        </Tag>
                                    }
                                >
                                    <Space direction="vertical" style={{ width: '100%' }}>
                                        {Object.entries(report.tablePatternStatistics || {}).map(([pattern, count]) => (
                                            <div key={pattern}>
                                                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
                                                    <span>{pattern}</span>
                                                    <span style={{ color: '#666' }}>
                                                        {count} ({calculatePercentage(count, report.totalTables)}%)
                                                    </span>
                                                </div>
                                                <Progress
                                                    percent={calculatePercentage(count, report.totalTables)}
                                                    showInfo={false}
                                                    strokeColor={pattern === report.dominantTablePattern ? '#52c41a' : '#1890ff'}
                                                />
                                            </div>
                                        ))}
                                    </Space>
                                </Card>
                            </Col>
                            <Col span={12}>
                                <Card
                                    title="Column Naming Pattern"
                                    size="small"
                                    extra={
                                        <Tag color="green" icon={<CheckCircleOutlined />}>
                                            {report.dominantColumnPattern}
                                        </Tag>
                                    }
                                >
                                    <Space direction="vertical" style={{ width: '100%' }}>
                                        {Object.entries(report.columnPatternStatistics || {}).map(([pattern, count]) => (
                                            <div key={pattern}>
                                                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
                                                    <span>{pattern}</span>
                                                    <span style={{ color: '#666' }}>
                                                        {count} ({calculatePercentage(count, report.totalColumns)}%)
                                                    </span>
                                                </div>
                                                <Progress
                                                    percent={calculatePercentage(count, report.totalColumns)}
                                                    showInfo={false}
                                                    strokeColor={pattern === report.dominantColumnPattern ? '#52c41a' : '#1890ff'}
                                                />
                                            </div>
                                        ))}
                                    </Space>
                                </Card>
                            </Col>
                        </Row>

                        {/* Recommendations */}
                        {report.recommendations && report.recommendations.length > 0 && (
                            <Card
                                title="Recommendations"
                                size="small"
                                style={{ marginBottom: 16 }}
                            >
                                <Table
                                    dataSource={report.recommendations}
                                    columns={recommendationColumns}
                                    rowKey={(record, index) => `rec-${index}`}
                                    pagination={false}
                                    size="small"
                                />
                            </Card>
                        )}

                        {/* Inconsistencies Table */}
                        {report.inconsistencies && report.inconsistencies.length > 0 ? (
                            <Card title="Inconsistencies" size="small">
                                <Alert
                                    message="Naming Convention Inconsistencies"
                                    description="These objects don't follow the dominant naming pattern. Review suggestions before applying changes."
                                    type="warning"
                                    showIcon
                                    style={{ marginBottom: 16 }}
                                />
                                <Table
                                    dataSource={report.inconsistencies}
                                    columns={inconsistencyColumns}
                                    rowKey={(record, index) => `inc-${record.table}-${record.column}-${index}`}
                                    pagination={{
                                        pageSize: 10,
                                        showSizeChanger: true,
                                        showTotal: (total) => `Total ${total} inconsistencies`,
                                    }}
                                    size="small"
                                    scroll={{ x: 'max-content' }}
                                />
                            </Card>
                        ) : (
                            <Empty
                                description="No naming inconsistencies found"
                                image={Empty.PRESENTED_IMAGE_SIMPLE}
                            >
                                <div style={{ color: '#52c41a', marginTop: 8 }}>
                                    <CheckCircleOutlined /> Your database follows consistent naming conventions!
                                </div>
                            </Empty>
                        )}

                        {/* Tips */}
                        <Card size="small" style={{ marginTop: 16, backgroundColor: '#f5f5f5' }}>
                            <div style={{ fontSize: 12 }}>
                                <div style={{ fontWeight: 500, marginBottom: 8 }}>💡 Best Practices:</div>
                                <ul style={{ margin: 0, paddingLeft: 20, color: '#666' }}>
                                    <li>Maintain consistent naming conventions across your schema</li>
                                    <li>Test rename scripts in a non-production environment first</li>
                                    <li>Update application code after renaming database objects</li>
                                    <li>Consider using sp_rename with caution - it doesn't update references</li>
                                    <li>Document your naming conventions for team consistency</li>
                                </ul>
                            </div>
                        </Card>
                    </div>
                ) : null}
            </Modal>

            {/* SQL Preview Modal */}
            <Modal
                title={selectedTitle}
                open={sqlModalVisible}
                onCancel={() => setSqlModalVisible(false)}
                width={900}
                footer={[
                    <Button
                        key="copy"
                        type="primary"
                        icon={<CopyOutlined />}
                        onClick={() => {
                            copyToClipboard(selectedSql);
                            setSqlModalVisible(false);
                        }}
                    >
                        Copy SQL
                    </Button>,
                    <Button key="close" onClick={() => setSqlModalVisible(false)}>
                        Close
                    </Button>,
                ]}
            >
                <Alert
                    message="Review Before Executing"
                    description="This script will rename database objects. Test in non-production first and update application code accordingly."
                    type="warning"
                    showIcon
                    style={{ marginBottom: 12 }}
                />
                <TextArea
                    value={selectedSql}
                    rows={20}
                    readOnly
                    style={{ fontFamily: 'monospace', fontSize: 12 }}
                />
            </Modal>
        </>
    );
};

export default NamingConventionReport;
