import { Modal, Alert, List, Tag, Space, Empty, Spin } from 'antd';
import { WarningOutlined, InfoCircleOutlined, CloseCircleOutlined } from '@ant-design/icons';

const SEVERITY_CONFIG = {
    Critical: {
        color: 'error',
        icon: <CloseCircleOutlined />,
        tagColor: 'red',
    },
    Warning: {
        color: 'warning',
        icon: <WarningOutlined />,
        tagColor: 'orange',
    },
    Info: {
        color: 'info',
        icon: <InfoCircleOutlined />,
        tagColor: 'blue',
    },
};

const HealthReport = ({ visible, onClose, health, loading }) => {
    if (loading) {
        return (
            <Modal
                title="Health Report"
                open={visible}
                onCancel={onClose}
                footer={null}
                width={800}
            >
                <div style={{ textAlign: 'center', padding: '40px 0' }}>
                    <Spin size="large" />
                </div>
            </Modal>
        );
    }

    const getSummaryType = () => {
        if (!health || health.totalIssues === 0) return 'success';
        if (health.criticalCount > 0) return 'error';
        if (health.warningCount > 0) return 'warning';
        return 'info';
    };

    return (
        <Modal
            title="Database Health Report"
            open={visible}
            onCancel={onClose}
            footer={null}
            width={800}
        >
            {!health || health.totalIssues === 0 ? (
                <Alert
                    message="Database is healthy"
                    description="No issues found in the database schema."
                    type="success"
                    showIcon
                />
            ) : (
                <>
                    <Alert
                        message={`${health.totalIssues} issue${health.totalIssues > 1 ? 's' : ''} found`}
                        description={
                            <Space>
                                {health.criticalCount > 0 && (
                                    <Tag color="red">{health.criticalCount} Critical</Tag>
                                )}
                                {health.warningCount > 0 && (
                                    <Tag color="orange">{health.warningCount} Warning</Tag>
                                )}
                                {health.infoCount > 0 && (
                                    <Tag color="blue">{health.infoCount} Info</Tag>
                                )}
                            </Space>
                        }
                        type={getSummaryType()}
                        showIcon
                        style={{ marginBottom: 16 }}
                    />

                    <List
                        dataSource={health.issues}
                        renderItem={(issue) => {
                            const config = SEVERITY_CONFIG[issue.severity] || SEVERITY_CONFIG.Info;
                            return (
                                <List.Item>
                                    <List.Item.Meta
                                        avatar={
                                            <div style={{ fontSize: 24, color: config.tagColor }}>
                                                {config.icon}
                                            </div>
                                        }
                                        title={
                                            <Space>
                                                <Tag color={config.tagColor}>{issue.severity}</Tag>
                                                <Tag>{issue.type}</Tag>
                                                <span style={{ fontWeight: 500 }}>
                                                    {issue.table}
                                                    {issue.column && `.${issue.column}`}
                                                </span>
                                            </Space>
                                        }
                                        description={
                                            <Space direction="vertical" size={4} style={{ width: '100%' }}>
                                                <div>{issue.description}</div>
                                                {issue.recommendation && (
                                                    <div style={{ color: '#1890ff', fontSize: 12 }}>
                                                        💡 {issue.recommendation}
                                                    </div>
                                                )}
                                            </Space>
                                        }
                                    />
                                </List.Item>
                            );
                        }}
                    />
                </>
            )}
        </Modal>
    );
};

export default HealthReport;
