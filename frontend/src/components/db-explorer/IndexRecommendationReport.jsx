import { useState } from 'react';
import { Card, Table, Tag, Space, Statistic, Row, Col, Button, Empty, Spin, Alert, Modal, Input, Tooltip, message } from 'antd';
import { ThunderboltOutlined, WarningOutlined, CheckCircleOutlined, CopyOutlined, EyeOutlined, InfoCircleOutlined } from '@ant-design/icons';
import { useIndexRecommendationsQuery } from '../../api/dbExplorer/queries';

const { TextArea } = Input;

const IndexRecommendationReport = ({ connectionId, visible, onClose }) => {
    const [sqlModalVisible, setSqlModalVisible] = useState(false);
    const [selectedSql, setSelectedSql] = useState('');
    const [selectedTitle, setSelectedTitle] = useState('');

    const { data: report, isLoading, error } = useIndexRecommendationsQuery(
        connectionId,
        { enabled: visible }
    );

    const copyToClipboard = (text, label = 'SQL') => {
        navigator.clipboard.writeText(text);
        message.success(`${label} copied to clipboard`);
    };

    const showSqlModal = (sql, title) => {
        setSelectedSql(sql);
        setSelectedTitle(title);
        setSqlModalVisible(true);
    };

    const getTypeColor = (type) => {
        const colors = {
            'Missing FK Index': 'red',
            'Missing Filter Index': 'orange',
            'Composite Index': 'blue',
            'Redundant Index': 'default',
            'Covering Index': 'green',
        };
        return colors[type] || 'default';
    };

    const getImpactColor = (impact) => {
        const colors = {
            'high': 'red',
            'medium': 'orange',
            'low': 'default',
        };
        return colors[impact] || 'default';
    };

    const columns = [
        {
            title: 'Type',
            dataIndex: 'type',
            key: 'type',
            width: '15%',
            render: (type) => <Tag color={getTypeColor(type)}>{type}</Tag>,
        },
        {
            title: 'Table',
            dataIndex: 'tableName',
            key: 'tableName',
            width: '15%',
            render: (text) => <span style={{ fontFamily: 'monospace' }}>{text}</span>,
        },
        {
            title: 'Index Name',
            dataIndex: 'indexName',
            key: 'indexName',
            width: '15%',
            render: (text) => <span style={{ fontFamily: 'monospace', fontSize: 12 }}>{text}</span>,
        },
        {
            title: 'Reason',
            dataIndex: 'reason',
            key: 'reason',
            width: '20%',
            ellipsis: true,
        },
        {
            title: 'Impact',
            dataIndex: 'impact',
            key: 'impact',
            width: '10%',
            render: (impact) => (
                <Tag color={getImpactColor(impact)}>
                    {impact.toUpperCase()}
                </Tag>
            ),
        },
        {
            title: 'Improvement',
            dataIndex: 'estimatedImprovement',
            key: 'estimatedImprovement',
            width: '15%',
            render: (text) => (
                <span style={{ color: '#52c41a', fontWeight: 500 }}>
                    {text}
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
                            onClick={() => showSqlModal(record.sqlScript, `${record.type} - ${record.tableName}`)}
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

    return (
        <>
            <Modal
                title={
                    <Space>
                        <ThunderboltOutlined style={{ color: '#1890ff' }} />
                        <span>Index Recommendations</span>
                    </Space>
                }
                open={visible}
                onCancel={onClose}
                width={1200}
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
                        <div style={{ marginTop: 16 }}>Analyzing indexes...</div>
                    </div>
                ) : error ? (
                    <Alert
                        message="Failed to load index recommendations"
                        description={error.response?.data?.details || error.message}
                        type="error"
                        showIcon
                    />
                ) : report ? (
                    <div>
                        {/* Summary Statistics */}
                        <Row gutter={16} style={{ marginBottom: 24 }}>
                            <Col span={8}>
                                <Card size="small">
                                    <Statistic
                                        title="Missing Indexes"
                                        value={report.missingIndexCount}
                                        valueStyle={{ color: '#ff4d4f' }}
                                        prefix={<WarningOutlined />}
                                    />
                                </Card>
                            </Col>
                            <Col span={8}>
                                <Card size="small">
                                    <Statistic
                                        title="Redundant Indexes"
                                        value={report.redundantIndexCount}
                                        valueStyle={{ color: '#faad14' }}
                                        prefix={<InfoCircleOutlined />}
                                    />
                                </Card>
                            </Col>
                            <Col span={8}>
                                <Card size="small">
                                    <Statistic
                                        title="Optimization Opportunities"
                                        value={report.optimizationCount}
                                        valueStyle={{ color: '#1890ff' }}
                                        prefix={<CheckCircleOutlined />}
                                    />
                                </Card>
                            </Col>
                        </Row>

                        {/* Info Alert */}
                        {report.recommendations && report.recommendations.length > 0 && (
                            <Alert
                                message="Index Optimization Recommendations"
                                description="These recommendations are based on metadata analysis. Review and test in a non-production environment before applying."
                                type="info"
                                showIcon
                                style={{ marginBottom: 16 }}
                            />
                        )}

                        {/* Recommendations Table */}
                        {report.recommendations && report.recommendations.length > 0 ? (
                            <>
                                <Table
                                    dataSource={report.recommendations}
                                    columns={columns}
                                    rowKey={(record, index) => `${record.tableName}-${record.indexName}-${index}`}
                                    pagination={{
                                        pageSize: 10,
                                        showSizeChanger: true,
                                        showTotal: (total) => `Total ${total} recommendations`,
                                    }}
                                    size="small"
                                    scroll={{ x: 'max-content' }}
                                />

                                {/* Bulk Apply Script */}
                                <Card
                                    title="Bulk Apply Script"
                                    size="small"
                                    style={{ marginTop: 16 }}
                                    extra={
                                        <Button
                                            icon={<CopyOutlined />}
                                            onClick={() => copyToClipboard(
                                                report.recommendations.map(r => r.sqlScript).join('\n\n'),
                                                'All SQL scripts'
                                            )}
                                            size="small"
                                        >
                                            Copy All
                                        </Button>
                                    }
                                >
                                    <Alert
                                        message="Production-Ready Script"
                                        description="All scripts use ONLINE = ON for minimal downtime. Review and test before applying to production."
                                        type="warning"
                                        showIcon
                                        style={{ marginBottom: 12 }}
                                    />
                                    <TextArea
                                        value={report.recommendations.map(r => r.sqlScript).join('\n\n')}
                                        rows={12}
                                        readOnly
                                        style={{ fontFamily: 'monospace', fontSize: 12 }}
                                    />
                                </Card>
                            </>
                        ) : (
                            <Empty
                                description="No index recommendations found"
                                image={Empty.PRESENTED_IMAGE_SIMPLE}
                            >
                                <div style={{ color: '#52c41a', marginTop: 8 }}>
                                    <CheckCircleOutlined /> Your database indexes are well optimized!
                                </div>
                            </Empty>
                        )}

                        {/* Tips */}
                        <Card size="small" style={{ marginTop: 16, backgroundColor: '#f5f5f5' }}>
                            <div style={{ fontSize: 12 }}>
                                <div style={{ fontWeight: 500, marginBottom: 8 }}>💡 Best Practices:</div>
                                <ul style={{ margin: 0, paddingLeft: 20, color: '#666' }}>
                                    <li>Test index changes in a non-production environment first</li>
                                    <li>Monitor query performance before and after applying indexes</li>
                                    <li>Consider maintenance windows for large tables</li>
                                    <li>ONLINE = ON allows concurrent access during index creation</li>
                                    <li>Remove redundant indexes to improve write performance</li>
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
                width={800}
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
                <TextArea
                    value={selectedSql}
                    rows={15}
                    readOnly
                    style={{ fontFamily: 'monospace', fontSize: 13 }}
                />
            </Modal>
        </>
    );
};

export default IndexRecommendationReport;
