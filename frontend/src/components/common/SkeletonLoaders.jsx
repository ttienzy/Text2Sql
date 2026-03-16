/**
 * Reusable Skeleton Loading Components
 * Provides consistent loading states across the application
 */
import { Skeleton, Card, List, Space } from 'antd';

/**
 * Chat Message Skeleton
 */
export const ChatMessageSkeleton = ({ count = 3 }) => (
    <div style={{ padding: '16px' }}>
        {Array.from({ length: count }).map((_, index) => (
            <div key={index} style={{ marginBottom: 24 }}>
                <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12 }}>
                    <Skeleton.Avatar size="small" active />
                    <div style={{ flex: 1 }}>
                        <Skeleton.Input size="small" style={{ width: 80, marginBottom: 8 }} active />
                        <Skeleton paragraph={{ rows: 2, width: ['100%', '80%'] }} active />
                    </div>
                </div>
            </div>
        ))}
    </div>
);

/**
 * Connection Card Skeleton
 */
export const ConnectionCardSkeleton = ({ count = 6 }) => (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: 16 }}>
        {Array.from({ length: count }).map((_, index) => (
            <Card key={index} size="small">
                <Skeleton active paragraph={{ rows: 3 }} />
            </Card>
        ))}
    </div>
);

/**
 * Conversation List Skeleton
 */
export const ConversationListSkeleton = ({ count = 5 }) => {
    // Ensure count is a valid number
    const safeCount = Math.max(1, Math.min(count || 5, 10));

    return (
        <List
            dataSource={Array.from({ length: safeCount }, (_, index) => ({
                key: `skeleton-${index}`,
                id: `skeleton-${index}`
            }))}
            renderItem={(item, index) => (
                <List.Item key={item.key} style={{ padding: '8px 12px' }}>
                    <div style={{ width: '100%' }}>
                        <Skeleton.Input size="small" style={{ width: '70%', marginBottom: 4 }} active />
                        <Skeleton.Input size="small" style={{ width: '40%' }} active />
                    </div>
                </List.Item>
            )}
        />
    );
};
/**
 * Schema Browser Skeleton
 */
export const SchemaBrowserSkeleton = ({ count = 3 }) => (
    <div>
        {Array.from({ length: count }).map((_, index) => (
            <div key={index} style={{ marginBottom: 16, padding: 12, border: '1px solid #f0f0f0', borderRadius: 6 }}>
                <div style={{ display: 'flex', alignItems: 'center', marginBottom: 8 }}>
                    <Skeleton.Avatar size="small" shape="square" active />
                    <Skeleton.Input size="small" style={{ width: 120, marginLeft: 8 }} active />
                </div>
                <div style={{ paddingLeft: 24 }}>
                    {Array.from({ length: 3 }).map((_, colIndex) => (
                        <div key={colIndex} style={{ display: 'flex', alignItems: 'center', marginBottom: 4 }}>
                            <Skeleton.Avatar size="small" shape="square" style={{ width: 16, height: 16 }} active />
                            <Skeleton.Input size="small" style={{ width: 80, marginLeft: 8, marginRight: 8 }} active />
                            <Skeleton.Input size="small" style={{ width: 60 }} active />
                        </div>
                    ))}
                </div>
            </div>
        ))}
    </div>
);

/**
 * Dashboard Chart Skeleton
 */
export const DashboardChartSkeleton = ({ height = 300 }) => (
    <Card>
        <div style={{ marginBottom: 16 }}>
            <Skeleton.Input style={{ width: 150 }} />
        </div>
        <div style={{ height, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <Skeleton.Image style={{ width: '100%', height: '100%' }} />
        </div>
    </Card>
);

/**
 * Table Skeleton
 */
export const TableSkeleton = ({ rows = 5, columns = 4 }) => (
    <div>
        {/* Table Header */}
        <div style={{ display: 'flex', padding: '12px 16px', borderBottom: '1px solid #f0f0f0' }}>
            {Array.from({ length: columns }).map((_, index) => (
                <div key={index} style={{ flex: 1, marginRight: 16 }}>
                    <Skeleton.Input size="small" style={{ width: '80%' }} active />
                </div>
            ))}
        </div>

        {/* Table Rows */}
        {Array.from({ length: rows }).map((_, rowIndex) => (
            <div key={rowIndex} style={{ display: 'flex', padding: '12px 16px', borderBottom: '1px solid #f9f9f9' }}>
                {Array.from({ length: columns }).map((_, colIndex) => (
                    <div key={colIndex} style={{ flex: 1, marginRight: 16 }}>
                        <Skeleton.Input size="small" style={{ width: colIndex === 0 ? '90%' : '70%' }} active />
                    </div>
                ))}
            </div>
        ))}
    </div>
);

/**
 * Info Panel Skeleton
 */
export const InfoPanelSkeleton = () => (
    <div style={{ padding: 12 }}>
        {/* Connection Info */}
        <div style={{ marginBottom: 16 }}>
            <Skeleton.Input style={{ width: 120, marginBottom: 8 }} active />
            <Space direction="vertical" size="small" style={{ width: '100%' }}>
                <Skeleton.Input size="small" style={{ width: '80%' }} active />
                <Skeleton.Input size="small" style={{ width: '60%' }} active />
                <Skeleton.Input size="small" style={{ width: '70%' }} active />
            </Space>
        </div>

        {/* Quota Progress */}
        <div style={{ marginBottom: 16 }}>
            <Skeleton.Input style={{ width: 100, marginBottom: 8 }} active />
            <Skeleton active paragraph={{ rows: 2 }} />
        </div>

        {/* Schema Browser */}
        <div>
            <Skeleton.Input style={{ width: 120, marginBottom: 8 }} active />
            <SchemaBrowserSkeleton count={2} />
        </div>
    </div>
);