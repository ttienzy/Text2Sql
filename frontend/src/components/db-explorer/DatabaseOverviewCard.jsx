import { Card, Tag, Space, Statistic, Row, Col, Button, Spin, Alert, Tooltip } from 'antd';
import { DatabaseOutlined, TableOutlined, WarningOutlined, ReloadOutlined, ClockCircleOutlined, CheckCircleOutlined, HistoryOutlined } from '@ant-design/icons';
import { formatNumber } from '../../utils/formatters';

const DatabaseOverviewCard = ({ overview, loading, error, onRefresh, onViewHealth, onViewChanges, onModuleClick, selectedModule }) => {
    if (loading) {
        return (
            <Card>
                <div style={{ textAlign: 'center', padding: '40px 0' }}>
                    <Spin size="large" />
                    <div style={{ marginTop: 16 }}>Analyzing database...</div>
                </div>
            </Card>
        );
    }

    if (error) {
        return (
            <Card>
                <Alert
                    message="Failed to load database overview"
                    description={error.message}
                    type="error"
                    showIcon
                />
            </Card>
        );
    }

    if (!overview) {
        return null;
    }

    const getStatusColor = () => {
        if (overview.issueCount === 0) return 'success';
        if (overview.issueCount < 5) return 'warning';
        return 'error';
    };

    const getStatusIcon = () => {
        if (overview.issueCount === 0) return <CheckCircleOutlined />;
        return <WarningOutlined />;
    };

    const formatLastAnalyzed = (timestamp) => {
        if (!timestamp) return 'Never';
        const date = new Date(timestamp);
        const now = new Date();
        const diffMs = now - date;
        const diffMins = Math.floor(diffMs / 60000);
        const diffHours = Math.floor(diffMs / 3600000);
        const diffDays = Math.floor(diffMs / 86400000);

        if (diffMins < 1) return 'Just now';
        if (diffMins < 60) return `${diffMins} minute${diffMins > 1 ? 's' : ''} ago`;
        if (diffHours < 24) return `${diffHours} hour${diffHours > 1 ? 's' : ''} ago`;
        return `${diffDays} day${diffDays > 1 ? 's' : ''} ago`;
    };

    return (
        <Card
            title={
                <Space>
                    <DatabaseOutlined />
                    <span>{overview.domain || 'Database Overview'}</span>
                    <Tag color={getStatusColor()} icon={getStatusIcon()}>
                        {overview.issueCount === 0 ? '🟢 Healthy' : `${overview.issueCount} Issues`}
                    </Tag>
                </Space>
            }
            extra={
                <Space>
                    <Tooltip title="Last analyzed">
                        <span style={{ fontSize: 12, color: '#999' }}>
                            <ClockCircleOutlined /> {formatLastAnalyzed(overview.scannedAt)}
                        </span>
                    </Tooltip>
                    <Button icon={<ReloadOutlined />} onClick={onRefresh} size="small">
                        Refresh
                    </Button>
                    <Button icon={<HistoryOutlined />} onClick={onViewChanges} size="small">
                        Changes
                    </Button>
                </Space>
            }
        >
            <div style={{ marginBottom: 16 }}>
                <p style={{ color: '#666', marginBottom: 16, fontStyle: 'italic' }}>"{overview.summary}"</p>
            </div>

            <Row gutter={16} style={{ marginBottom: 16 }}>
                <Col span={6}>
                    <Statistic
                        title="Tables"
                        value={overview.tableCount}
                        prefix={<TableOutlined />}
                    />
                </Col>
                <Col span={6}>
                    <Statistic
                        title="Columns"
                        value={overview.columnCount}
                    />
                </Col>
                <Col span={6}>
                    <Statistic
                        title="Total Rows"
                        value={formatNumber(overview.totalRows)}
                    />
                </Col>
                <Col span={6}>
                    <Statistic
                        title="Confidence"
                        value={Math.round(overview.confidence * 100)}
                        suffix="%"
                    />
                </Col>
            </Row>

            {overview.modules && overview.modules.length > 0 && (
                <div style={{ marginBottom: 16 }}>
                    <div style={{ marginBottom: 8, fontWeight: 500 }}>
                        Modules: <span style={{ fontSize: 12, color: '#999', fontWeight: 'normal' }}>
                            (Click to filter tables)
                        </span>
                    </div>
                    <Space wrap>
                        {overview.modules.map((module) => (
                            <Tag
                                key={module.name}
                                color={selectedModule === module.name ? 'purple' : 'blue'}
                                style={{
                                    cursor: 'pointer',
                                    fontSize: 13,
                                    padding: '4px 12px',
                                    border: selectedModule === module.name ? '2px solid #722ed1' : 'none'
                                }}
                                onClick={() => onModuleClick(module.name)}
                            >
                                📦 {module.name} ({module.tables.length})
                            </Tag>
                        ))}
                        {selectedModule && (
                            <Tag
                                color="default"
                                style={{ cursor: 'pointer' }}
                                onClick={() => onModuleClick(null)}
                            >
                                ✕ Clear filter
                            </Tag>
                        )}
                    </Space>
                </div>
            )}

            {overview.issueCount > 0 && (
                <Alert
                    message={`${overview.issueCount} health issues found`}
                    type="warning"
                    showIcon
                    icon={<WarningOutlined />}
                    action={
                        <Button size="small" onClick={onViewHealth}>
                            View Details
                        </Button>
                    }
                />
            )}
        </Card>
    );
};

export default DatabaseOverviewCard;
