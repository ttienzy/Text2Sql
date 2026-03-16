/**
 * PageSkeleton - Loading states for entire pages
 */
import { Skeleton, Card, Row, Col, Divider } from 'antd';

/**
 * Settings Page Skeleton
 */
export const SettingsPageSkeleton = () => (
    <div>
        <Skeleton.Input style={{ width: 200, marginBottom: 8 }} />
        <Skeleton.Input size="small" style={{ width: 400, marginBottom: 16 }} />

        <Divider />

        {/* Tabs skeleton */}
        <div style={{ marginBottom: 16 }}>
            <div style={{ display: 'flex', gap: 24, marginBottom: 16 }}>
                <Skeleton.Button style={{ width: 100 }} />
                <Skeleton.Button style={{ width: 80 }} />
                <Skeleton.Button style={{ width: 120 }} />
            </div>
        </div>

        {/* Dashboard content skeleton */}
        <div style={{ padding: '16px 0' }}>
            <Row gutter={[16, 16]}>
                <Col xs={24} lg={12}>
                    <Card>
                        <Skeleton active paragraph={{ rows: 4 }} />
                    </Card>
                </Col>
                <Col xs={24} lg={12}>
                    <Card>
                        <Skeleton active paragraph={{ rows: 4 }} />
                    </Card>
                </Col>
            </Row>

            <div style={{ marginTop: 16 }}>
                <Card>
                    <Skeleton active paragraph={{ rows: 6 }} />
                </Card>
            </div>

            <div style={{ marginTop: 16 }}>
                <Card>
                    <Skeleton active paragraph={{ rows: 8 }} />
                </Card>
            </div>
        </div>
    </div>
);

/**
 * Connections Page Skeleton
 */
export const ConnectionsPageSkeleton = () => (
    <div>
        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 16 }}>
            <Skeleton.Input style={{ width: 200 }} />
            <Skeleton.Button style={{ width: 120 }} />
        </div>

        {/* Table skeleton */}
        <Card>
            <div>
                {/* Header */}
                <div style={{ display: 'flex', padding: '12px 16px', borderBottom: '1px solid #f0f0f0' }}>
                    {Array.from({ length: 6 }).map((_, index) => (
                        <div key={index} style={{ flex: 1, marginRight: 16 }}>
                            <Skeleton.Input size="small" style={{ width: '80%' }} />
                        </div>
                    ))}
                </div>

                {/* Rows */}
                {Array.from({ length: 5 }).map((_, rowIndex) => (
                    <div key={rowIndex} style={{ display: 'flex', padding: '12px 16px', borderBottom: '1px solid #f9f9f9' }}>
                        {Array.from({ length: 6 }).map((_, colIndex) => (
                            <div key={colIndex} style={{ flex: 1, marginRight: 16 }}>
                                <Skeleton.Input size="small" style={{ width: colIndex === 0 ? '90%' : '70%' }} />
                            </div>
                        ))}
                    </div>
                ))}
            </div>
        </Card>
    </div>
);

/**
 * Chat Page Skeleton
 */
export const ChatPageSkeleton = () => (
    <div style={{ height: 'calc(100vh - 64px)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <div style={{ textAlign: 'center' }}>
            <Skeleton.Avatar size={64} style={{ marginBottom: 16 }} />
            <Skeleton.Input style={{ width: 300, marginBottom: 8 }} />
            <Skeleton.Input size="small" style={{ width: 200 }} />
        </div>
    </div>
);