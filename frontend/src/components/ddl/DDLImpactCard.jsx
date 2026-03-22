import React from 'react';
import { Modal, Alert, Tag, Divider, Space, Typography, Card, Row, Col, List } from 'antd';
import {
    InfoCircleOutlined,
    CheckCircleOutlined,
    WarningOutlined,
    ThunderboltOutlined,
    DatabaseOutlined,
    LockOutlined,
    RiseOutlined
} from '@ant-design/icons';
import SqlBlock from '../chat/SqlBlock';

const { Text } = Typography;

/**
 * Impact analysis modal for DDL operations (CREATE INDEX, ALTER TABLE, etc.)
 * Shows DDL script, impact metrics, benefits, and warnings
 */
const DDLImpactCard = ({ open, preview, onConfirm, onCancel, loading }) => {
    if (!preview) return null;

    const { impact } = preview;

    // Format bytes to human-readable
    const formatBytes = (bytes) => {
        if (bytes === 0) return '0 Bytes';
        const k = 1024;
        const sizes = ['Bytes', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
    };

    // Format duration
    const formatDuration = (duration) => {
        if (!duration) return 'N/A';
        const parts = duration.split(':');
        const seconds = parseInt(parts[2] || 0);
        const minutes = parseInt(parts[1] || 0);
        if (minutes > 0) return `${minutes}m ${seconds}s`;
        return `${seconds}s`;
    };

    return (
        <Modal
            title={
                <Space>
                    <InfoCircleOutlined style={{ color: '#1890ff' }} />
                    <span>DDL Operation Impact Analysis</span>
                </Space>
            }
            open={open}
            onOk={onConfirm}
            onCancel={onCancel}
            confirmLoading={loading}
            okText={loading ? 'Executing...' : 'Confirm & Execute'}
            cancelText="Cancel"
            width={800}
            maskClosable={false}
        >
            {/* Operation Type */}
            <Space style={{ marginBottom: 16 }}>
                <Tag color="blue">{preview.operationType}</Tag>
                <Tag>Target: {preview.targetObject}</Tag>
            </Space>

            {/* DDL Script */}
            <div style={{ marginBottom: 16 }}>
                <Text type="secondary">DDL Script:</Text>
                <SqlBlock sql={preview.ddlScript} />
            </div>

            <Divider>Impact Analysis</Divider>

            {/* Impact Metrics */}
            <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
                {impact.estimatedStorageBytes > 0 && (
                    <Col span={12}>
                        <Card size="small">
                            <Space>
                                <DatabaseOutlined style={{ fontSize: 20, color: '#1890ff' }} />
                                <div>
                                    <Text type="secondary" style={{ fontSize: 12 }}>Storage</Text>
                                    <div><Text strong>{formatBytes(impact.estimatedStorageBytes)}</Text></div>
                                </div>
                            </Space>
                        </Card>
                    </Col>
                )}

                {impact.estimatedLockDuration && (
                    <Col span={12}>
                        <Card size="small">
                            <Space>
                                <LockOutlined style={{ fontSize: 20, color: '#faad14' }} />
                                <div>
                                    <Text type="secondary" style={{ fontSize: 12 }}>Lock Time</Text>
                                    <div><Text strong>{formatDuration(impact.estimatedLockDuration)}</Text></div>
                                </div>
                            </Space>
                        </Card>
                    </Col>
                )}

                {impact.estimatedPerformanceGain > 1 && (
                    <Col span={12}>
                        <Card size="small">
                            <Space>
                                <ThunderboltOutlined style={{ fontSize: 20, color: '#52c41a' }} />
                                <div>
                                    <Text type="secondary" style={{ fontSize: 12 }}>Performance</Text>
                                    <div><Text strong>{impact.estimatedPerformanceGain}x faster</Text></div>
                                </div>
                            </Space>
                        </Card>
                    </Col>
                )}

                {impact.writeOverheadPercent > 0 && (
                    <Col span={12}>
                        <Card size="small">
                            <Space>
                                <RiseOutlined style={{ fontSize: 20, color: '#faad14' }} />
                                <div>
                                    <Text type="secondary" style={{ fontSize: 12 }}>Write Overhead</Text>
                                    <div><Text strong>+{impact.writeOverheadPercent}%</Text></div>
                                </div>
                            </Space>
                        </Card>
                    </Col>
                )}
            </Row>

            {/* Benefits */}
            {impact.benefits && impact.benefits.length > 0 && (
                <Alert
                    message="Benefits"
                    description={
                        <List
                            size="small"
                            dataSource={impact.benefits}
                            renderItem={item => (
                                <List.Item>
                                    <CheckCircleOutlined style={{ color: '#52c41a', marginRight: 8 }} />
                                    {item}
                                </List.Item>
                            )}
                        />
                    }
                    type="success"
                    showIcon={false}
                    style={{ marginBottom: 16 }}
                />
            )}

            {/* Warnings */}
            {impact.warnings && impact.warnings.length > 0 && (
                <Alert
                    message="Warnings"
                    description={
                        <List
                            size="small"
                            dataSource={impact.warnings}
                            renderItem={item => (
                                <List.Item>
                                    <WarningOutlined style={{ color: '#faad14', marginRight: 8 }} />
                                    {item}
                                </List.Item>
                            )}
                        />
                    }
                    type="warning"
                    showIcon={false}
                    style={{ marginBottom: 16 }}
                />
            )}

            {/* Related Objects */}
            {preview.relatedObjects && preview.relatedObjects.length > 0 && (
                <div style={{ marginBottom: 16 }}>
                    <Text type="secondary">Related Objects:</Text>
                    <div style={{ marginTop: 8 }}>
                        <Space wrap>
                            {preview.relatedObjects.map((obj, index) => (
                                <Tag key={index}>{obj}</Tag>
                            ))}
                        </Space>
                    </div>
                </div>
            )}

            <Divider />

            {/* Confirmation Message */}
            <Alert
                message="Please review the impact analysis"
                description="This DDL operation will modify your database structure. Ensure you understand the implications before proceeding."
                type="info"
                showIcon
            />
        </Modal>
    );
};

export default DDLImpactCard;
