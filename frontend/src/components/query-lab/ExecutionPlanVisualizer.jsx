import React from 'react';
import { Card, Tree, Tag, Tooltip, Row, Col, Statistic } from 'antd';
import {
    ThunderboltOutlined,
    DatabaseOutlined,
    ClockCircleOutlined,
    CheckCircleOutlined,
    WarningOutlined
} from '@ant-design/icons';

/**
 * ExecutionPlanVisualizer - Visual comparison of execution plans
 * Shows side-by-side tree view with color-coded costs
 */
const ExecutionPlanVisualizer = ({ planComparison }) => {
    if (!planComparison) {
        return null;
    }

    const getCostColor = (cost) => {
        if (cost > 50) return '#ff4d4f'; // Red - expensive
        if (cost > 10) return '#faad14'; // Orange - moderate
        return '#52c41a'; // Green - cheap
    };

    const getOperatorIcon = (type) => {
        if (type.includes('Seek')) return '🎯';
        if (type.includes('Scan')) return '📊';
        if (type.includes('Join')) return '🔗';
        if (type.includes('Sort')) return '📈';
        if (type.includes('Aggregate')) return '📐';
        if (type.includes('Filter')) return '🔍';
        return '⚙️';
    };

    const getOperatorTooltip = (type) => {
        if (type.includes('Seek')) return 'Index Seek - Fast, uses index to find specific rows';
        if (type.includes('Scan')) return 'Index/Table Scan - Slower, reads all rows';
        if (type.includes('Join')) return 'Join operation - Combines data from multiple tables';
        if (type.includes('Sort')) return 'Sort operation - Orders result set';
        if (type.includes('Aggregate')) return 'Aggregate - Performs calculations (SUM, COUNT, etc.)';
        if (type.includes('Filter')) return 'Filter - Applies WHERE conditions';
        return 'Database operation';
    };

    const buildTreeData = (operators) => {
        if (!operators || operators.length === 0) return [];

        // Build tree structure from flat operator list
        // For simplicity, we'll show operators as a flat list
        // In a real implementation, you'd parse the parent-child relationships
        return operators.map((op, index) => ({
            title: (
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span style={{ fontSize: 16 }}>{getOperatorIcon(op.type)}</span>
                    <span style={{ fontWeight: 'bold' }}>{op.type}</span>
                    <Tag color={getCostColor(op.estimatedCost)}>
                        Cost: {op.estimatedCost.toFixed(2)}
                    </Tag>
                    <span style={{ color: '#666', fontSize: 12 }}>
                        Rows: {op.estimatedRows.toLocaleString()}
                    </span>
                    {op.objectName && (
                        <Tooltip title={`Object: ${op.objectName}${op.indexName ? ` (${op.indexName})` : ''}`}>
                            <Tag icon={<DatabaseOutlined />} color="blue">
                                {op.objectName.split('.').pop()}
                            </Tag>
                        </Tooltip>
                    )}
                </div>
            ),
            key: `op-${index}`,
            children: []
        }));
    };

    const improvementColor = planComparison.isImproved ? '#52c41a' : '#666';
    const improvementIcon = planComparison.isImproved ?
        <CheckCircleOutlined style={{ color: '#52c41a' }} /> :
        <WarningOutlined style={{ color: '#faad14' }} />;

    return (
        <Card
            title={
                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <ThunderboltOutlined />
                    <span>Execution Plan Comparison</span>
                </div>
            }
            style={{ marginTop: 16 }}
        >
            {/* Summary Statistics */}
            <Row gutter={16} style={{ marginBottom: 24 }}>
                <Col span={6}>
                    <Statistic
                        title="Original Cost"
                        value={planComparison.originalCost.toFixed(2)}
                        valueStyle={{ color: '#ff4d4f' }}
                        prefix={<ClockCircleOutlined />}
                    />
                </Col>
                <Col span={6}>
                    <Statistic
                        title="Optimized Cost"
                        value={planComparison.optimizedCost.toFixed(2)}
                        valueStyle={{ color: '#52c41a' }}
                        prefix={<ClockCircleOutlined />}
                    />
                </Col>
                <Col span={6}>
                    <Statistic
                        title="Improvement"
                        value={`${planComparison.improvementPercentage.toFixed(1)}%`}
                        valueStyle={{ color: improvementColor }}
                        prefix={improvementIcon}
                    />
                </Col>
                <Col span={6}>
                    <Statistic
                        title="Performance"
                        value={planComparison.improvementDescription}
                        valueStyle={{ color: improvementColor, fontSize: 16 }}
                    />
                </Col>
            </Row>

            {/* Warnings */}
            {(planComparison.originalWarnings?.length > 0 || planComparison.optimizedWarnings?.length > 0) && (
                <div style={{ marginBottom: 16 }}>
                    {planComparison.originalWarnings?.length > 0 && (
                        <div style={{ marginBottom: 8 }}>
                            <Tag color="warning" icon={<WarningOutlined />}>
                                Original Query Warnings: {planComparison.originalWarnings.join(', ')}
                            </Tag>
                        </div>
                    )}
                    {planComparison.optimizedWarnings?.length > 0 && (
                        <div>
                            <Tag color="warning" icon={<WarningOutlined />}>
                                Optimized Query Warnings: {planComparison.optimizedWarnings.join(', ')}
                            </Tag>
                        </div>
                    )}
                </div>
            )}

            {/* Side-by-side plan comparison */}
            <Row gutter={16}>
                <Col span={12}>
                    <Card
                        type="inner"
                        title="Original Plan"
                        style={{ background: '#fff1f0' }}
                        bodyStyle={{ maxHeight: '300px', overflow: 'auto' }}
                    >
                        <div style={{ maxHeight: '250px', overflow: 'auto' }}>
                            <Tree
                                treeData={buildTreeData(planComparison.originalOperators)}
                                defaultExpandAll
                                showLine
                                showIcon={false}
                            />
                        </div>
                        <div style={{
                            marginTop: 16,
                            padding: 12,
                            background: '#fff',
                            borderRadius: 4,
                            border: '1px solid #ffccc7'
                        }}>
                            <strong>Total Cost:</strong> {planComparison.originalCost.toFixed(2)}
                        </div>
                    </Card>
                </Col>

                <Col span={12}>
                    <Card
                        type="inner"
                        title="Optimized Plan"
                        style={{ background: '#f6ffed' }}
                        bodyStyle={{ maxHeight: '300px', overflow: 'auto' }}
                    >
                        <div style={{ maxHeight: '250px', overflow: 'auto' }}>
                            <Tree
                                treeData={buildTreeData(planComparison.optimizedOperators)}
                                defaultExpandAll
                                showLine
                                showIcon={false}
                            />
                        </div>
                        <div style={{
                            marginTop: 16,
                            padding: 12,
                            background: '#fff',
                            borderRadius: 4,
                            border: '1px solid #b7eb8f'
                        }}>
                            <strong>Total Cost:</strong> {planComparison.optimizedCost.toFixed(2)}
                            {planComparison.isImproved && (
                                <div style={{ color: '#52c41a', marginTop: 4 }}>
                                    ⚡ {planComparison.improvementDescription}
                                </div>
                            )}
                        </div>
                    </Card>
                </Col>
            </Row>

            {/* Legend */}
            <div style={{ marginTop: 16, padding: 12, background: '#fafafa', borderRadius: 4 }}>
                <strong>Legend:</strong>
                <div style={{ display: 'flex', gap: 16, marginTop: 8, flexWrap: 'wrap' }}>
                    <span>🎯 Index Seek (fast)</span>
                    <span>📊 Index/Table Scan (slow)</span>
                    <span>🔗 Join</span>
                    <span>📈 Sort</span>
                    <span>⚙️ Other Operations</span>
                    <Tag color="#52c41a">Low Cost (&lt;10)</Tag>
                    <Tag color="#faad14">Moderate Cost (10-50)</Tag>
                    <Tag color="#ff4d4f">High Cost (&gt;50)</Tag>
                </div>
            </div>
        </Card>
    );
};

export default ExecutionPlanVisualizer;
