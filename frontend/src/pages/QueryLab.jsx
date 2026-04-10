import { useState } from 'react';
import { message, Alert, Space, Switch, Tooltip } from 'antd';
import { ThunderboltOutlined, DatabaseOutlined, LineChartOutlined } from '@ant-design/icons';
import useConnectionStore from '../store/connectionStore';
import { useOptimizeQueryMutation, useOptimizeQueryWithPlanMutation } from '../api/queryOptimizer';
import {
    SqlEditor,
    OptimizedSqlViewer,
    AntiPatternList,
    ExecutionPlanVisualizer,
    DataSkewIndicator,
    PreFlightAnalysisPanel,
    AutoFixConfirmModal,
} from '../components/query-lab';

const QueryLab = () => {
    const { activeConnection } = useConnectionStore();
    const [originalSql, setOriginalSql] = useState('');
    const [optimizationResult, setOptimizationResult] = useState(null);
    const [includeExecutionPlan, setIncludeExecutionPlan] = useState(false);
    const [autoFixResult, setAutoFixResult] = useState(null);
    const [showAutoFixModal, setShowAutoFixModal] = useState(false);

    const hasCriticalIssues = (data) =>
        data?.severity?.toLowerCase() === 'critical' ||
        (data?.detectedIssues || []).some((issue) => {
            const level = issue?.severity?.toLowerCase();
            return level === 'critical' || level === 'error';
        });

    const hasAnyIssues = (data) => (data?.detectedIssues || []).length > 0;

    const notifyOptimizationOutcome = (data, successMessage) => {
        if (hasCriticalIssues(data)) {
            message.error('Query has blocking issues. Please review Analysis Results.');
            return;
        }

        if (data.isChanged) {
            message.success(successMessage);
            return;
        }

        if (hasAnyIssues(data)) {
            message.warning('No rewrite applied. Please review detected issues.');
            return;
        }

        message.info('Query is already optimal');
    };

    // Mutation for basic optimization
    const optimizeMutation = useOptimizeQueryMutation({
        onSuccess: (data) => {
            setOptimizationResult(data);

            // Handle auto-fix results
            if (data.autoFixResult) {
                if (data.autoFixResult.confidenceLevel === 'High' && !data.autoFixResult.requiresSemanticValidation) {
                    // High confidence - auto-apply
                    message.success('Auto-fix applied successfully!');
                    setOriginalSql(data.autoFixResult.fixedSql);
                } else if (data.autoFixResult.confidenceLevel === 'Medium' && data.autoFixResult.requiresSemanticValidation) {
                    // Medium confidence - show confirmation modal
                    setAutoFixResult(data.autoFixResult);
                    setShowAutoFixModal(true);
                }
            }

            notifyOptimizationOutcome(data, 'Query optimized successfully!');
        },
        onError: (error) => {
            const errorMsg = error.response?.data?.error || error.message || 'Failed to optimize query';
            message.error(`Optimization failed: ${errorMsg}`);
        },
    });

    // Mutation for optimization with execution plan
    const optimizeWithPlanMutation = useOptimizeQueryWithPlanMutation({
        onSuccess: (data) => {
            setOptimizationResult(data);

            // Handle auto-fix results
            if (data.autoFixResult) {
                if (data.autoFixResult.confidenceLevel === 'High' && !data.autoFixResult.requiresSemanticValidation) {
                    // High confidence - auto-apply
                    message.success('Auto-fix applied successfully!');
                    setOriginalSql(data.autoFixResult.fixedSql);
                } else if (data.autoFixResult.confidenceLevel === 'Medium' && data.autoFixResult.requiresSemanticValidation) {
                    // Medium confidence - show confirmation modal
                    setAutoFixResult(data.autoFixResult);
                    setShowAutoFixModal(true);
                }
            }

            notifyOptimizationOutcome(data, 'Query optimized with execution plan comparison!');
        },
        onError: (error) => {
            const errorMsg = error.response?.data?.error || error.message || 'Failed to optimize query';
            message.error(`Optimization failed: ${errorMsg}`);
        },
    });

    const handleAnalyze = () => {
        if (!originalSql.trim()) {
            message.warning('Please enter a SQL query');
            return;
        }

        if (!activeConnection) {
            message.warning('Please select a database connection');
            return;
        }

        // Use appropriate mutation based on toggle
        if (includeExecutionPlan) {
            optimizeWithPlanMutation.mutate({
                sql: originalSql,
                connectionId: activeConnection.id,
            });
        } else {
            optimizeMutation.mutate({
                sql: originalSql,
                connectionId: activeConnection.id,
            });
        }
    };

    const handleClearResult = () => {
        setOptimizationResult(null);
        setAutoFixResult(null);
        setShowAutoFixModal(false);
    };

    const handleAutoFixConfirm = () => {
        if (autoFixResult) {
            setOriginalSql(autoFixResult.fixedSql);
            message.success('Auto-fix applied successfully!');
            setShowAutoFixModal(false);
            setAutoFixResult(null);
        }
    };

    const handleAutoFixCancel = () => {
        setShowAutoFixModal(false);
        setAutoFixResult(null);
    };

    // Check if connection is selected
    if (!activeConnection) {
        return (
            <div style={{ padding: 24 }}>
                <Alert
                    message="No Connection Selected"
                    description="Please select a database connection to use Query Lab."
                    type="warning"
                    showIcon
                />
            </div>
        );
    }

    return (
        <div style={{ height: 'calc(100vh - 64px)', display: 'flex', flexDirection: 'column', background: '#f5f5f5' }}>
            {/* Header */}
            <div style={{
                padding: '16px 24px',
                background: '#fff',
                borderBottom: '1px solid #f0f0f0',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between'
            }}>
                <Space>
                    <ThunderboltOutlined style={{ fontSize: 24, color: '#1890ff' }} />
                    <div>
                        <div style={{ fontSize: 18, fontWeight: 600 }}>Query Lab — SQL Optimizer</div>
                        <div style={{ fontSize: 12, color: '#999' }}>
                            <DatabaseOutlined style={{ marginRight: 4 }} />
                            {activeConnection.name}
                        </div>
                    </div>
                </Space>

                {/* Execution Plan Toggle */}
                <Tooltip title="Compare execution plans (adds ~200ms overhead)">
                    <Space>
                        <LineChartOutlined style={{ color: includeExecutionPlan ? '#1890ff' : '#999' }} />
                        <span style={{ fontSize: 14 }}>Compare Execution Plans</span>
                        <Switch
                            checked={includeExecutionPlan}
                            onChange={setIncludeExecutionPlan}
                        />
                    </Space>
                </Tooltip>
            </div>

            {/* Main Content - Vertical Split View (2 Columns) */}
            <div style={{
                flex: 1,
                display: 'flex',
                flexDirection: 'row',
                overflow: 'hidden',
                gap: 0
            }}>
                {/* Left Column - Original SQL */}
                <div style={{
                    flex: 1,
                    display: 'flex',
                    flexDirection: 'column',
                    borderRight: '2px solid #e8e8e8',
                    overflow: 'hidden'
                }}>
                    <div style={{
                        padding: '12px 16px',
                        background: '#fafafa',
                        borderBottom: '1px solid #f0f0f0',
                        fontWeight: 600,
                        fontSize: 14,
                        color: '#262626'
                    }}>
                        📝 Original Query
                    </div>
                    <div style={{ flex: 1, overflow: 'auto', padding: 16 }}>
                        <SqlEditor
                            value={originalSql}
                            onChange={setOriginalSql}
                            onAnalyze={handleAnalyze}
                            loading={optimizeMutation.isPending || optimizeWithPlanMutation.isPending}
                        />
                    </div>
                </div>

                {/* Right Column - Optimized SQL */}
                <div style={{
                    flex: 1,
                    display: 'flex',
                    flexDirection: 'column',
                    overflow: 'hidden'
                }}>
                    <div style={{
                        padding: '12px 16px',
                        background: '#f6ffed',
                        borderBottom: '1px solid #b7eb8f',
                        fontWeight: 600,
                        fontSize: 14,
                        color: '#389e0d',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between'
                    }}>
                        <span>✨ Optimized Query</span>
                        {optimizationResult?.isChanged && (
                            <span style={{
                                fontSize: 12,
                                fontWeight: 400,
                                color: '#52c41a',
                                background: '#f6ffed',
                                padding: '2px 8px',
                                borderRadius: 4,
                                border: '1px solid #b7eb8f'
                            }}>
                                Improved
                            </span>
                        )}
                    </div>
                    <div style={{ flex: 1, overflow: 'auto', padding: 16 }}>
                        <OptimizedSqlViewer
                            result={optimizationResult}
                            loading={optimizeMutation.isPending || optimizeWithPlanMutation.isPending}
                            onClear={handleClearResult}
                        />
                    </div>
                </div>
            </div>

            {/* Bottom Panel - Analysis Details (Collapsible) */}
            {optimizationResult && (
                <div style={{
                    background: '#fff',
                    borderTop: '2px solid #e8e8e8',
                    maxHeight: '45vh',
                    overflow: 'auto',
                    boxShadow: '0 -2px 8px rgba(0,0,0,0.06)'
                }}>
                    <div style={{
                        padding: '12px 24px',
                        background: '#fafafa',
                        borderBottom: '1px solid #f0f0f0',
                        fontWeight: 600,
                        fontSize: 14,
                        color: '#262626',
                        display: 'flex',
                        alignItems: 'center',
                        gap: 8
                    }}>
                        <LineChartOutlined style={{ color: '#1890ff' }} />
                        Analysis Results
                    </div>

                    <div style={{ padding: '16px 24px' }}>
                        {/* Pre-Flight Analysis Panel (Phase 4) */}
                        {optimizationResult.preFlightAnalysis && (
                            <div style={{ marginBottom: 16 }}>
                                <PreFlightAnalysisPanel analysis={optimizationResult.preFlightAnalysis} />
                            </div>
                        )}

                        {/* Anti-Pattern List */}
                        <div style={{ marginBottom: 16 }}>
                            <AntiPatternList result={optimizationResult} />
                        </div>

                        {/* Execution Plan Visualizer (Sprint 2) */}
                        {optimizationResult.planComparison && (
                            <div style={{ marginBottom: 16 }}>
                                <ExecutionPlanVisualizer planComparison={optimizationResult.planComparison} />
                            </div>
                        )}

                        {/* Data Skew Indicator (Sprint 2 + Phase 4 PSP Awareness) */}
                        {optimizationResult.columnStats && (
                            <div style={{ marginBottom: 16 }}>
                                <DataSkewIndicator
                                    columnStats={optimizationResult.columnStats}
                                    pspActive={optimizationResult.preFlightAnalysis?.pspActive}
                                />
                            </div>
                        )}
                    </div>
                </div>
            )}

            {/* Auto-Fix Confirmation Modal */}
            <AutoFixConfirmModal
                visible={showAutoFixModal}
                fixResult={autoFixResult}
                onConfirm={handleAutoFixConfirm}
                onCancel={handleAutoFixCancel}
            />
        </div>
    );
};

export default QueryLab;
