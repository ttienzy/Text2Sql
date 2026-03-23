import React from 'react';
import { Modal, Alert, Tag, Divider, Space, Typography, Card, List } from 'antd';
import { StopOutlined, BulbOutlined } from '@ant-design/icons';
import SqlBlock from '../chat/SqlBlock';

const { Text, Title } = Typography;

/**
 * Alert dialog for FORBIDDEN operations (DELETE, DROP, TRUNCATE)
 * Shows rejection reason and safe alternatives
 */
const ForbiddenAlert = ({ open, result, onClose }) => {
    if (!result) return null;

    return (
        <Modal
            title={
                <Space>
                    <StopOutlined style={{ color: '#ff4d4f' }} />
                    <span>Operation Blocked</span>
                </Space>
            }
            open={open}
            onOk={onClose}
            onCancel={onClose}
            okText="I Understand"
            cancelButtonProps={{ style: { display: 'none' } }}
            width={700}
            maskClosable={false}
        >
            {/* Rejection Reason */}
            <Alert
                message="This operation is not allowed"
                description={result.rejectionReason || result.forbiddenReason}
                type="error"
                showIcon
                style={{ marginBottom: 16 }}
            />

            {/* Detected Patterns */}
            {result.detectedPatterns && result.detectedPatterns.length > 0 && (
                <div style={{ marginBottom: 16 }}>
                    <Text type="secondary">Detected dangerous patterns:</Text>
                    <div style={{ marginTop: 8 }}>
                        <Space wrap>
                            {result.detectedPatterns.map((pattern, index) => (
                                <Tag key={index} color="error">{pattern}</Tag>
                            ))}
                        </Space>
                    </div>
                </div>
            )}

            <Divider />

            {/* Safe Alternatives */}
            <div style={{ marginBottom: 16 }}>
                <Space style={{ marginBottom: 12 }}>
                    <BulbOutlined style={{ color: '#1890ff', fontSize: 20 }} />
                    <Title level={5} style={{ margin: 0 }}>Safe Alternatives</Title>
                </Space>
                <Text type="secondary">
                    Instead of permanently deleting data, consider these safer approaches:
                </Text>
            </div>

            <Space direction="vertical" style={{ width: '100%' }} size="middle">
                {result.safeAlternatives && result.safeAlternatives.map((alternative, index) => (
                    <Card key={index} size="small" style={{ backgroundColor: '#f5f5f5' }}>
                        <Title level={5} style={{ color: '#1890ff', marginTop: 0 }}>
                            {index + 1}. {alternative.title}
                        </Title>
                        <Text>{alternative.description}</Text>

                        {alternative.exampleSql && (
                            <div style={{ marginTop: 12 }}>
                                <Text type="secondary" style={{ fontSize: 12 }}>Example SQL:</Text>
                                <SqlBlock sql={alternative.exampleSql} />
                            </div>
                        )}
                    </Card>
                ))}
            </Space>

            <Divider />

            {/* Why Not Allowed */}
            <Alert
                message="Why are delete operations blocked?"
                description="To protect important data, this system does not support permanent deletion operations (DELETE, DROP, TRUNCATE). The alternative methods above allow you to 'deactivate' data while preserving history and the ability to recover."
                type="info"
                showIcon
            />
        </Modal>
    );
};

export default ForbiddenAlert;
