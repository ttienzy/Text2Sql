import React, { useEffect, useState, useRef } from 'react';
import { Modal, Alert, Tag, Divider, Space, Typography, Progress } from 'antd';
import { WarningOutlined, CheckCircleOutlined, ClockCircleOutlined } from '@ant-design/icons';
import SqlBlock from '../chat/SqlBlock';

const { Text } = Typography;

/**
 * Confirmation modal for WRITE operations (INSERT/UPDATE/DELETE)
 * Shows SQL preview, estimated affected rows, warnings, and a countdown timer.
 *
 * ✅ FIX: Added countdown timer that mirrors the backend polling timeout.
 *         Calls onCancel() automatically when timer hits 0 to prevent silent >60s hang.
 */
const WriteConfirmationModal = ({ open, preview, onConfirm, onCancel, loading }) => {
    const [countdown, setCountdown] = useState(null);
    const timerRef = useRef(null);

    // Start countdown when modal opens with a valid preview
    useEffect(() => {
        if (open && preview?.timeoutSeconds) {
            const total = preview.timeoutSeconds;
            setCountdown(total);

            timerRef.current = setInterval(() => {
                setCountdown((prev) => {
                    if (prev <= 1) {
                        clearInterval(timerRef.current);
                        // Auto-cancel when time runs out — prevents backend 60s hang
                        onCancel?.();
                        return 0;
                    }
                    return prev - 1;
                });
            }, 1000);
        }

        return () => {
            clearInterval(timerRef.current);
            setCountdown(null);
        };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [open, preview?.confirmId]); // Re-run only on new confirmation, not every render

    if (!preview) return null;

    const isUpdate  = preview.operationType === 'Update';
    const isInsert  = preview.operationType === 'Insert';
    const isDelete  = preview.operationType === 'Delete';
    const total     = preview.timeoutSeconds ?? 30;
    const pct       = countdown != null ? Math.round((countdown / total) * 100) : 100;
    const isUrgent  = countdown != null && countdown <= 10;

    const opLabel   = isUpdate ? 'Update' : isDelete ? 'Delete' : 'Insert';
    const opColor   = isUpdate ? 'orange' : isDelete ? 'red' : 'green';
    const progressStatus = isUrgent ? 'exception' : 'active';

    return (
        <Modal
            title={
                <Space>
                    <WarningOutlined style={{ color: '#faad14' }} />
                    <span>Confirm {opLabel} Operation</span>
                    {countdown != null && (
                        <Tag
                            icon={<ClockCircleOutlined />}
                            color={isUrgent ? 'red' : 'blue'}
                            style={{ marginLeft: 8 }}
                        >
                            {countdown}s remaining
                        </Tag>
                    )}
                </Space>
            }
            open={open}
            onOk={onConfirm}
            onCancel={onCancel}
            confirmLoading={loading}
            okText={loading ? 'Executing...' : `Confirm ${opLabel}`}
            okButtonProps={{
                danger: isUpdate || isDelete,
                disabled: loading || (!preview.hasWhereClause && isUpdate),
            }}
            cancelText="Cancel"
            width={700}
            maskClosable={false}
        >
            {/* Countdown progress bar */}
            {countdown != null && (
                <Progress
                    percent={pct}
                    status={progressStatus}
                    showInfo={false}
                    strokeColor={isUrgent ? '#ff4d4f' : '#1890ff'}
                    style={{ marginBottom: 16 }}
                    size="small"
                />
            )}

            {/* Operation Type */}
            <Space style={{ marginBottom: 16 }}>
                <Tag color={opColor}>{preview.operationType}</Tag>
                <Tag>Target: {preview.targetTable}</Tag>
                {preview.riskLevel && (
                    <Tag color={preview.riskLevel === 'Critical' ? 'red' : preview.riskLevel === 'High' ? 'orange' : 'default'}>
                        Risk: {preview.riskLevel}
                    </Tag>
                )}
            </Space>

            {/* SQL Preview */}
            <div style={{ marginBottom: 16 }}>
                <Text type="secondary">SQL Statement:</Text>
                <SqlBlock sql={preview.sqlStatement} />
            </div>

            {/* Estimated Impact */}
            <Alert
                message="Estimated Impact"
                description={
                    <div>
                        <Text>
                            Approximately <Text strong>{preview.estimatedAffectedRows ?? 1}</Text> row(s) will be affected
                        </Text>
                        {preview.affectedColumns?.length > 0 && (
                            <div style={{ marginTop: 8 }}>
                                <Text type="secondary">
                                    Affected columns: {preview.affectedColumns.join(', ')}
                                </Text>
                            </div>
                        )}
                    </div>
                }
                type="info"
                showIcon
                style={{ marginBottom: 16 }}
            />

            {/* Warnings */}
            {preview.warnings?.length > 0 && (
                <Alert
                    message="Warnings"
                    description={
                        <ul style={{ margin: 0, paddingLeft: 20 }}>
                            {preview.warnings.map((warning, index) => (
                                <li key={index}>{warning}</li>
                            ))}
                        </ul>
                    }
                    type="warning"
                    showIcon
                    style={{ marginBottom: 16 }}
                />
            )}

            {/* WHERE Clause validation for UPDATE/DELETE */}
            {(isUpdate || isDelete) && (
                <Alert
                    message={
                        preview.hasWhereClause
                            ? `✓ WHERE clause detected — specific rows will be ${isDelete ? 'deleted' : 'updated'}`
                            : `✗ No WHERE clause — this would ${isDelete ? 'delete' : 'update'} ALL rows (BLOCKED)`
                    }
                    type={preview.hasWhereClause ? 'success' : 'error'}
                    showIcon
                    icon={preview.hasWhereClause ? <CheckCircleOutlined /> : <WarningOutlined />}
                    style={{ marginBottom: 16 }}
                />
            )}

            <Divider />

            {/* Confirmation Message */}
            <Alert
                message={countdown != null && countdown <= 10 ? `⏰ ${countdown}s left — please confirm or cancel` : 'Please review carefully'}
                description={
                    countdown != null && countdown <= 10
                        ? 'The operation will be automatically cancelled when the timer expires.'
                        : 'This operation will modify data in your database. Make sure you understand the impact before confirming.'
                }
                type={countdown != null && countdown <= 10 ? 'warning' : 'info'}
                showIcon
            />
        </Modal>
    );
};

export default WriteConfirmationModal;
