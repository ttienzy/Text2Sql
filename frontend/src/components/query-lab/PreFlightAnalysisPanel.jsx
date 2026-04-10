import React, { useState } from 'react';
import { Card, Alert, Tag, Typography, Space, Divider, Badge, Button, message } from 'antd';
import {
    WarningOutlined,
    CheckCircleOutlined,
    ThunderboltOutlined,
    DatabaseOutlined,
    CopyOutlined,
    InfoCircleOutlined,
} from '@ant-design/icons';

const { Text, Paragraph } = Typography;

/**
 * PreFlightAnalysisPanel - Displays execution plan analysis results
 * Shows cost drivers, warnings, missing indexes, and implicit conversions
 */
const PreFlightAnalysisPanel = ({ analysis }) => {
    if (!analysis) {
        return null;
    }

    // If execution plan is not available
    if (!analysis.canGetExecutionPlan) {
        return (
            <Card
                title={
                    <Space>
                        <DatabaseOutlined />
                        <span>Execution Plan Analysis</span>
                    </Space>
                }
                style={{ marginTop: 16 }}
            >
                <Alert
                    message="Execution Plan Unavailable"
                    description="Missing VIEW DATABASE STATE permission. Grant this permission to enable full execution plan analysis."
                    type="warning"
                    showIcon
                    icon={<WarningOutlined />}
                />
            </Card>
        );
    }

    const getSeverityColor = (severity) => {
        switch (severity) {
            case 'Critical': return 'red';
            case 'High': return 'orange';
            case 'Medium': return 'gold';
            case 'Info': return 'blue';
            default: return 'default';
        }
    };

    const handleCopyIndex = (statement) => {
        navigator.clipboard.writeText(statement);
        message.success('Index statement copied to clipboard');
    };

    return (
        <Card
            title={
                <Space>
                    <ThunderboltOutlined />
                    <span>Execution Plan Analysis</span>
                </Space>
            }
            style={{ marginTop: 16 }}
        >
            {/* Summary Row */}
            <div style={{
                display: 'flex',
                gap: 24,
                padding: 16,
                background: '#fafafa',
                borderRadius: 4,
                marginBottom: 16
            }}>
                <div>
                    <Text type="secondary" style={{ fontSize: 12 }}>Estimated Cost</Text>
                    <div style={{ fontSize: 24, fontWeight: 600, color: '#1890ff' }}>
                        {analysis.estimatedCost?.toFixed(2) || '0.00'}
                    </div>
                </div>
                <Divider type="vertical" style={{ height: 'auto' }} />
                <div>
                    <Text type="secondary" style={{ fontSize: 12 }}>Estimated Rows</Text>
                    <div style={{ fontSize: 24, fontWeight: 600 }}>
                        {analysis.estimatedRows?.toLocaleString() || '0'}
                    </div>
                </div>
                <Divider type="vertical" style={{ height: 'auto' }} />
                <div>
                    <Text type="secondary" style={{ fontSize: 12 }}>Status</Text>
                    <div style={{ marginTop: 4 }}>
                        {analysis.needsOptimization ? (
                            <Badge status="warning" text="Needs Optimization" />
                        ) : (
                            <Badge status="success" text="Optimal" />
                        )}
                    </div>
                </div>
            </div>

            {/* Cost Drivers Section */}
            {analysis.costDrivers && analysis.costDrivers.length > 0 && (
                <div style={{ marginBottom: 16 }}>
                    <Text strong style={{ fontSize: 14, display: 'block', marginBottom: 8 }}>
                        <ThunderboltOutlined style={{ marginRight: 4, color: '#ff7a45' }} />
                        Top Cost Drivers
                    </Text>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                        {analysis.costDrivers.map((driver, index) => (
                            <div
                                key={index}
                                style={{
                                    padding: 12,
                                    background: '#fff',
                                    border: '1px solid #ffa940',
                                    borderLeft: '4px solid #ff7a45',
                                    borderRadius: 4
                                }}
                            >
                                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
                                    <Text strong>{driver.operatorType}</Text>
                                    <Space size="small">
                                        <Text type="secondary" style={{ fontSize: 12 }}>
                                            Cost: {driver.cost?.toFixed(2)}
                                        </Text>
                                        <Text type="secondary" style={{ fontSize: 12 }}>
                                            Rows: {driver.rows?.toLocaleString()}
                                        </Text>
                                    </Space>
                                </div>
                                <Text type="secondary" style={{ fontSize: 13 }}>
                                    {driver.description}
                                </Text>
                                {driver.recommendation && (
                                    <div style={{ marginTop: 8 }}>
                                        <Text italic style={{ fontSize: 12, color: '#1890ff' }}>
                                            💡 {driver.recommendation}
                                        </Text>
                                    </div>
                                )}
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {/* Plan Warnings Section */}
            {analysis.warnings && analysis.warnings.length > 0 && (
                <div style={{ marginBottom: 16 }}>
                    <Text strong style={{ fontSize: 14, display: 'block', marginBottom: 8 }}>
                        <WarningOutlined style={{ marginRight: 4, color: '#faad14' }} />
                        Plan Warnings
                    </Text>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                        {analysis.warnings.map((warning, index) => (
                            <Alert
                                key={index}
                                message={
                                    <Space>
                                        <Tag color={getSeverityColor(warning.severity)}>
                                            {warning.severity}
                                        </Tag>
                                        <Text>{warning.description}</Text>
                                    </Space>
                                }
                                description={
                                    <Text italic style={{ fontSize: 12 }}>
                                        {warning.recommendation}
                                    </Text>
                                }
                                type={warning.severity === 'Critical' ? 'error' : 'warning'}
                                showIcon
                                style={{ marginBottom: 0 }}
                            />
                        ))}
                    </div>
                </div>
            )}

            {/* Missing Index Recommendations */}
            {analysis.indexRecommendations && analysis.indexRecommendations.length > 0 && (
                <div style={{ marginBottom: 16 }}>
                    <Text strong style={{ fontSize: 14, display: 'block', marginBottom: 8 }}>
                        <DatabaseOutlined style={{ marginRight: 4, color: '#52c41a' }} />
                        Missing Index Recommendations
                    </Text>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                        {analysis.indexRecommendations.map((rec, index) => (
                            <div
                                key={index}
                                style={{
                                    padding: 12,
                                    background: '#f6ffed',
                                    border: '1px solid #b7eb8f',
                                    borderRadius: 4
                                }}
                            >
                                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
                                    <Space>
                                        <Text strong>{rec.tableName}</Text>
                                        <Badge
                                            count={`Impact: ${rec.impactPercentage?.toFixed(1)}%`}
                                            style={{ backgroundColor: '#52c41a' }}
                                        />
                                    </Space>
                                </div>
                                <div style={{ marginBottom: 8 }}>
                                    <Text type="secondary" style={{ fontSize: 12 }}>Key Columns: </Text>
                                    <Text style={{ fontSize: 12 }}>{rec.keyColumns?.join(', ')}</Text>
                                </div>
                                {rec.includeColumns && rec.includeColumns.length > 0 && (
                                    <div style={{ marginBottom: 8 }}>
                                        <Text type="secondary" style={{ fontSize: 12 }}>Include Columns: </Text>
                                        <Text style={{ fontSize: 12 }}>{rec.includeColumns.join(', ')}</Text>
                                    </div>
                                )}
                                <div style={{
                                    marginTop: 8,
                                    padding: 8,
                                    background: '#fff',
                                    borderRadius: 4,
                                    border: '1px solid #d9d9d9',
                                    position: 'relative'
                                }}>
                                    <Button
                                        type="text"
                                        size="small"
                                        icon={<CopyOutlined />}
                                        onClick={() => handleCopyIndex(rec.createStatement)}
                                        style={{ position: 'absolute', top: 4, right: 4 }}
                                    >
                                        Copy
                                    </Button>
                                    <pre style={{
                                        margin: 0,
                                        fontSize: 12,
                                        fontFamily: 'Consolas, Monaco, monospace',
                                        whiteSpace: 'pre-wrap',
                                        wordBreak: 'break-all',
                                        paddingRight: 60
                                    }}>
                                        {rec.createStatement}
                                    </pre>
                                </div>
                            </div>
                        ))}
                    </div>
                </div>
            )}

            {/* Implicit Conversions */}
            {analysis.implicitConversions && analysis.implicitConversions.length > 0 && (
                <Alert
                    message="Implicit Type Conversions Detected"
                    description={
                        <div>
                            <Paragraph style={{ marginBottom: 8 }}>
                                Implicit conversions can prevent index usage and degrade performance.
                            </Paragraph>
                            <ul style={{ marginBottom: 0, paddingLeft: 20 }}>
                                {analysis.implicitConversions.map((conv, index) => (
                                    <li key={index}>
                                        <Text strong>{conv.columnName}</Text>: {conv.fromType} → {conv.toType}
                                        <br />
                                        <Text type="secondary" style={{ fontSize: 12 }}>
                                            {conv.impact}
                                        </Text>
                                    </li>
                                ))}
                            </ul>
                        </div>
                    }
                    type="warning"
                    showIcon
                    icon={<InfoCircleOutlined />}
                />
            )}

            {/* Missing Statistics */}
            {analysis.missingStatistics && analysis.missingStatistics.length > 0 && (
                <Alert
                    message="Missing Statistics"
                    description={
                        <div>
                            <Paragraph style={{ marginBottom: 8 }}>
                                The following columns are missing statistics. Run UPDATE STATISTICS to improve query performance.
                            </Paragraph>
                            <Space wrap>
                                {analysis.missingStatistics.map((col, index) => (
                                    <Tag key={index} color="orange">{col}</Tag>
                                ))}
                            </Space>
                        </div>
                    }
                    type="warning"
                    showIcon
                    style={{ marginTop: 12 }}
                />
            )}

            {/* Stale Statistics Warning */}
            {analysis.hasStaleStatistics && (
                <Alert
                    message="Stale Statistics Detected"
                    description="Some statistics are outdated. Consider running UPDATE STATISTICS to ensure optimal query plans."
                    type="info"
                    showIcon
                    style={{ marginTop: 12 }}
                />
            )}
        </Card>
    );
};

export default PreFlightAnalysisPanel;
