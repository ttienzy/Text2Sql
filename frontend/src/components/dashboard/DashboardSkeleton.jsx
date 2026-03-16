/**
 * DashboardSkeleton - Loading states for dashboard components
 */
import { Skeleton, Card, Row, Col } from 'antd';

export const QuotaProgressSkeleton = ({ compact = false }) => {
    if (compact) {
        return (
            <div style={{ padding: 8 }}>
                <Skeleton.Input style={{ width: 100, marginBottom: 8 }} active />
                <Skeleton.Button style={{ width: '100%', height: 8, marginBottom: 8 }} active />
                <div style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <Skeleton.Input size="small" style={{ width: 60 }} active />
                    <Skeleton.Input size="small" style={{ width: 60 }} active />
                </div>
            </div>
        );
    }

    return (
        <Card>
            <div style={{ textAlign: 'center', padding: 16 }}>
                <Skeleton.Avatar size={120} active />
                <div style={{ marginTop: 16 }}>
                    <Row gutter={16}>
                        <Col span={8}>
                            <Skeleton.Input style={{ width: '100%' }} active />
                        </Col>
                        <Col span={8}>
                            <Skeleton.Input style={{ width: '100%' }} active />
                        </Col>
                        <Col span={8}>
                            <Skeleton.Input style={{ width: '100%' }} active />
                        </Col>
                    </Row>
                </div>
            </div>
        </Card>
    );
};

export const UsageChartSkeleton = () => (
    <Card>
        <div style={{ marginBottom: 16 }}>
            <Skeleton.Input style={{ width: 150 }} active />
        </div>
        <div style={{ height: 300, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Skeleton.Image style={{ width: '100%', height: '100%' }} active />
        </div>
        <div style={{ marginTop: 16, display: 'flex', justifyContent: 'space-between' }}>
            {Array.from({ length: 4 }).map((_, index) => (
                <div key={index} style={{ textAlign: 'center' }}>
                    <Skeleton.Input size="small" style={{ width: 60, marginBottom: 4 }} active />
                    <Skeleton.Input size="small" style={{ width: 80 }} active />
                </div>
            ))}
        </div>
    </Card>
);

export const UsageByModelSkeleton = () => (
    <Card>
        <Row gutter={16} style={{ marginBottom: 24 }}>
            {Array.from({ length: 3 }).map((_, index) => (
                <Col key={index} xs={24} sm={8}>
                    <Card size="small">
                        <Skeleton active paragraph={{ rows: 1 }} />
                    </Card>
                </Col>
            ))}
        </Row>

        <div style={{ marginBottom: 24 }}>
            <Skeleton.Input style={{ width: 150, marginBottom: 16 }} />
            <div style={{ height: 250 }}>
                <Skeleton.Image style={{ width: '100%', height: '100%' }} />
            </div>
        </div>

        <div>
            <Skeleton.Input style={{ width: 150, marginBottom: 16 }} />
            {Array.from({ length: 3 }).map((_, index) => (
                <div key={index} style={{ marginBottom: 16, padding: 16, background: '#fafafa', borderRadius: 8 }}>
                    <Skeleton active paragraph={{ rows: 2 }} />
                </div>
            ))}
        </div>
    </Card>
);

export const UsageByConversationSkeleton = () => (
    <Card>
        <div style={{ marginBottom: 16, padding: 16, background: '#fafafa', borderRadius: 8 }}>
            <Row gutter={16}>
                {Array.from({ length: 3 }).map((_, index) => (
                    <Col key={index} span={8}>
                        <Skeleton.Input size="small" style={{ width: '100%', marginBottom: 4 }} />
                        <Skeleton.Input style={{ width: '80%' }} />
                    </Col>
                ))}
            </Row>
        </div>

        {/* Table skeleton */}
        <div>
            {/* Header */}
            <div style={{ display: 'flex', padding: '12px 16px', borderBottom: '1px solid #f0f0f0' }}>
                {Array.from({ length: 5 }).map((_, index) => (
                    <div key={index} style={{ flex: 1, marginRight: 16 }}>
                        <Skeleton.Input size="small" style={{ width: '80%' }} />
                    </div>
                ))}
            </div>

            {/* Rows */}
            {Array.from({ length: 5 }).map((_, rowIndex) => (
                <div key={rowIndex} style={{ display: 'flex', padding: '12px 16px', borderBottom: '1px solid #f9f9f9' }}>
                    {Array.from({ length: 5 }).map((_, colIndex) => (
                        <div key={colIndex} style={{ flex: 1, marginRight: 16 }}>
                            <Skeleton.Input size="small" style={{ width: colIndex === 0 ? '90%' : '70%' }} />
                        </div>
                    ))}
                </div>
            ))}
        </div>
    </Card>
);