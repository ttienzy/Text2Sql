import React from 'react';
import { Modal, Alert, Tag, Space, Typography } from 'antd';
import { StopOutlined, WarningOutlined } from '@ant-design/icons';

const { Text, Paragraph } = Typography;

const ForbiddenAlert = ({ open, result, onClose }) => {
    if (!result) return null;

    const decodeHtml = (str) => {
        if (!str) return '';
        const txt = document.createElement("textarea");
        txt.innerHTML = str;
        return txt.value;
    };

    const cleanMessage = decodeHtml(result.userFacingMessage || result.rejectionReason || '');
    const lines = cleanMessage.split('\n').filter(line => line.trim());

    return (
        <Modal
            title={<Space><StopOutlined style={{ color: '#ff4d4f' }} /><span>Operation Blocked</span></Space>}
            open={open}
            onOk={onClose}
            onCancel={onClose}
            okText="I Understand"
            cancelButtonProps={{ style: { display: 'none' } }}
            width={700}
            maskClosable={false}
        >
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
            <Alert type="error" showIcon icon={<WarningOutlined />} message="Action Blocked" style={{ marginBottom: 16 }} />
            <div style={{ padding: '16px', backgroundColor: '#fafafa', borderRadius: '4px', marginBottom: 16 }}>
                {lines.map((line, index) => {
                    const isSql = line.match(/^(SELECT|UPDATE|INSERT|DELETE|CREATE|DROP|ALTER|TRUNCATE|WITH)/i);
                    if (isSql) {
                        return <pre key={index} style={{ backgroundColor: '#1e1e1e', color: '#d4d4d4', padding: '12px', borderRadius: '4px', fontSize: '13px', fontFamily: 'Consolas, Monaco, monospace', marginTop: 8, marginBottom: 8 }}>{line}</pre>;
                    }
                    return <Paragraph key={index} style={{ marginBottom: 8 }}>{line}</Paragraph>;
                })}
            </div>
            <Alert message="Why are delete operations blocked?" description="To protect data, this system blocks permanent deletion operations." type="info" showIcon />
        </Modal>
    );
};

export default ForbiddenAlert;
