import React from 'react';
import { Modal, Alert, Badge, Space, Typography, Divider, Collapse, Button, message } from 'antd';
import {
    WarningOutlined,
    CheckCircleOutlined,
    CopyOutlined,
    CodeOutlined,
} from '@ant-design/icons';

const { Text, Paragraph } = Typography;
const { Panel } = Collapse;

/**
 * AutoFixConfirmModal - Confirmation modal for medium-confidence auto-fixes
 * Shows diff view, semantic risks, and validation query
 * @param {boolean} visible - Whether modal is visible
 * @param {Object} fixResult - AutoFixResult object
 * @param {Function} onConfirm - Callback when user confirms
 * @param {Function} onCancel - Callback when user cancels
 */
const AutoFixConfirmModal = ({ visible, fixResult, onConfirm, onCancel }) => {
    if (!fixResult) {
        return null;
    }

    const handleCopy = (text, label) => {
        navigator.clipboard.writeText(text);
        message.success(`${label} copied to clipboard`);
    };

    const getConfidenceColor = (confidence) => {
        switch (confidence) {
            case 'High': return 'green';
            case 'Medium': return 'gold';
            case 'Low': return 'red';
            default: return 'default';
        }
    };

    return (
        <Modal
            title={
                <Space>
                    <WarningOutlined style={{ color: '#faad14' }} />
                    <span>Confirm Auto-Fix</span>
                </Space>
            }
            open={visible}
            onOk={onConfirm}
            onCancel={onCancel}
            width={900}
            okText="Apply Fix"
            cancelText="Cancel"
            okButtonProps={{ type: 'primary' }}
        >
            {/* Confidence Badge */}
            <div style={{ marginBottom: 16 }}>
                <Badge
                    count={`Confidence: ${fixResult.confidenceLevel}`}
                    style={{
                        backgroundColor: getConfidenceColor(fixResult.confidenceLevel),
                        fontSize: 14,
                        padding: '4px 12px',
                        height: 'auto'
                    }}
                />
            </div>

            {/* Semantic Validation Warning */}
            {fixResult.requiresSemanticValidation && (
                <Alert
                    message="Semantic Validation Required"
                    description="This fix requires semantic validation to ensure query results remain identical. Please review the changes carefully."
                    type="warning"
                    showIcon
                    style={{ marginBottom: 16 }}
                />
            )}

            {/* Diff View - Side by Side */}
            <div style={{ marginBottom: 16 }}>
                <Text strong style={{ display: 'block', marginBottom: 8 }}>
                    SQL Changes
                </Text>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
                    {/* Original SQL */}
                    <div>
                        <div style={{
                            padding: '4px 8px',
                            background: '#fff1f0',
                            border: '1px solid #ffccc7',
                            borderBottom: 'none',
                            borderRadius: '4px 4px 0 0',
                            fontSize: 12,
                            fontWeight: 600
                        }}>
                            Original SQL
                        </div>
                        <div style={{
                            padding: 12,
                            background: '#fff',
                            border: '1px solid #ffccc7',
                            borderRadius: '0 0 4px 4px',
                            maxHeight: 200,
                            overflow: 'auto',
                            position: 'relative'
                        }}>
                            <Button
                                type="text"
                                size="small"
                                icon={<CopyOutlined />}
                                onClick={() => handleCopy(fixResult.originalSql, 'Original SQL')}
                                style={{ position: 'absolute', top: 4, right: 4 }}
                            />
                            <pre style={{
                                margin: 0,
                                fontSize: 12,
                                fontFamily: 'Consolas, Monaco, monospace',
                                whiteSpace: 'pre-wrap',
                                wordBreak: 'break-all',
                                paddingRight: 40
                            }}>
                                {fixResult.originalSql}
                            </pre>
                        </div>
                    </div>

                    {/* Fixed SQL */}
                    <div>
                        <div style={{
                            padding: '4px 8px',
                            background: '#f6ffed',
                            border: '1px solid #b7eb8f',
                            borderBottom: 'none',
                            borderRadius: '4px 4px 0 0',
                            fontSize: 12,
                            fontWeight: 600
                        }}>
                            Fixed SQL
                        </div>
                        <div style={{
                            padding: 12,
                            background: '#fff',
                            border: '1px solid #b7eb8f',
                            borderRadius: '0 0 4px 4px',
                            maxHeight: 200,
                            overflow: 'auto',
                            position: 'relative'
                        }}>
                            <Button
                                type="text"
                                size="small"
                                icon={<CopyOutlined />}
                                onClick={() => handleCopy(fixResult.fixedSql, 'Fixed SQL')}
                                style={{ position: 'absolute', top: 4, right: 4 }}
                            />
                            <pre style={{
                                margin: 0,
                                fontSize: 12,
                                fontFamily: 'Consolas, Monaco, monospace',
                                whiteSpace: 'pre-wrap',
                                wordBreak: 'break-all',
                                paddingRight: 40
                            }}>
                                {fixResult.fixedSql}
                            </pre>
                        </div>
                    </div>
                </div>
            </div>

            {/* Semantic Risks */}
            {fixResult.semanticRisks && fixResult.semanticRisks.length > 0 && (
                <div style={{ marginBottom: 16 }}>
                    <Alert
                        message="Semantic Risks"
                        description={
                            <ul style={{ marginBottom: 0, paddingLeft: 20 }}>
                                {fixResult.semanticRisks.map((risk, index) => (
                                    <li key={index}>
                                        <Text>{risk}</Text>
                                    </li>
                                ))}
                            </ul>
                        }
                        type="warning"
                        showIcon
                        icon={<WarningOutlined />}
                    />
                </div>
            )}

            {/* Validation Query (Collapsible) */}
            {fixResult.validationQuery && (
                <Collapse
                    ghost
                    items={[
                        {
                            key: '1',
                            label: (
                                <Space>
                                    <CodeOutlined />
                                    <Text strong>Validation Query</Text>
                                </Space>
                            ),
                            children: (
                                <div style={{
                                    padding: 12,
                                    background: '#fafafa',
                                    border: '1px solid #d9d9d9',
                                    borderRadius: 4,
                                    position: 'relative'
                                }}>
                                    <Button
                                        type="text"
                                        size="small"
                                        icon={<CopyOutlined />}
                                        onClick={() => handleCopy(fixResult.validationQuery, 'Validation Query')}
                                        style={{ position: 'absolute', top: 4, right: 4 }}
                                    />
                                    <pre style={{
                                        margin: 0,
                                        fontSize: 12,
                                        fontFamily: 'Consolas, Monaco, monospace',
                                        whiteSpace: 'pre-wrap',
                                        wordBreak: 'break-all',
                                        paddingRight: 40
                                    }}>
                                        {fixResult.validationQuery}
                                    </pre>
                                </div>
                            ),
                        },
                    ]}
                />
            )}

            {/* Explanation */}
            {fixResult.explanation && (
                <div style={{ marginTop: 16 }}>
                    <Divider style={{ margin: '12px 0' }} />
                    <Text type="secondary" style={{ fontSize: 12 }}>
                        <strong>Explanation:</strong> {fixResult.explanation}
                    </Text>
                </div>
            )}
        </Modal>
    );
};

export default AutoFixConfirmModal;
