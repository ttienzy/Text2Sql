import React from 'react';
import { Modal, Alert, Tag, Divider, Space, Typography } from 'antd';
import { WarningOutlined, CheckCircleOutlined } from '@ant-design/icons';
import SqlBlock from '../chat/SqlBlock';

const { Text, Title } = Typography;

/**
 * Confirmation modal for WRITE operations (INSERT/UPDATE)
 * Shows SQL preview, estimated affected rows, and warnings
 */
const WriteConfirmationModal = ({ open, preview, onConfirm, onCancel, loading }) => {
    if (!preview) return null;

    const isUpdate = preview.operationType === 'Update';
    const isInsert = preview.operationType === 'Insert';

    return (
        <Modal
            title={
                <Space>
                    <WarningOutlined style={{ color: '#faad14' }} />
                    <span>Confirm {isUpdate ? 'Update' : 'Insert'} Operation</span>
                </Space>
            }
            open={open}
            onOk={onConfirm}
            onCancel={onCancel}
            confirmLoading={loading}
            okText={loading ? 'Executing...' : `Confirm ${isUpdate ? 'Update' : 'Insert'}`}
            okButtonProps={{
                danger: isUpdate,
                disabled: loading || (!preview.hasWhereClause && isUpdate)
            }}
            cancelText="Cancel"
            width={700}
            maskClosable={false}
        >
            {/* Operation Type */}
            <Space style={{ marginBottom: 16 }}>
                <Tag color={isUpdate ? 'orange' : 'green'}>{preview.operationType}</Tag>
                <Tag>Target: {preview.targetTable}</Tag>
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
                            Approximately <Text strong>{preview.estimatedAffectedRows}</Text> row(s) will be affected
                        </Text>
                        {preview.affectedColumns && preview.affectedColumns.length > 0 && (
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
            {preview.warnings && preview.warnings.length > 0 && (
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

            {/* WHERE Clause Info for UPDATE */}
            {isUpdate && (
                <Alert
                    message={
                        preview.hasWhereClause
                            ? '✓ WHERE clause detected - specific rows will be updated'
                            : '✗ No WHERE clause - this would update ALL rows (BLOCKED)'
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
                message="Please review carefully"
                description="This operation will modify data in your database. Make sure you understand the impact before confirming."
                type="info"
                showIcon
            />
        </Modal>
    );
};

export default WriteConfirmationModal;
