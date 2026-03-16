/**
 * LayoutSkeleton - Loading state for the main application layout
 */
import { Layout, Skeleton, Card } from 'antd';

const { Sider, Content } = Layout;

const LayoutSkeleton = () => {
    return (
        <Layout style={{ height: 'calc(100vh - 64px)', background: '#fff' }}>
            {/* Left Sidebar Skeleton */}
            <Sider
                width={280}
                theme="light"
                style={{
                    borderRight: '1px solid #f0f0f0',
                    overflow: 'hidden',
                }}
            >
                <div style={{ padding: 16 }}>
                    <Skeleton.Input style={{ width: '100%', marginBottom: 16 }} />
                    <Skeleton.Button style={{ width: '100%', marginBottom: 16 }} />

                    {/* Conversation list skeleton */}
                    {Array.from({ length: 5 }).map((_, index) => (
                        <div key={index} style={{ marginBottom: 12, padding: 8 }}>
                            <Skeleton.Input style={{ width: '80%', marginBottom: 4 }} />
                            <Skeleton.Input size="small" style={{ width: '50%' }} />
                        </div>
                    ))}
                </div>
            </Sider>

            {/* Main Chat Area Skeleton */}
            <Content style={{
                display: 'flex',
                flexDirection: 'column',
                overflow: 'hidden',
                flex: 1,
            }}>
                {/* Header */}
                <div style={{
                    padding: '12px 16px',
                    borderBottom: '1px solid #f0f0f0',
                    backgroundColor: '#fff',
                }}>
                    <Skeleton.Input style={{ width: 200 }} />
                </div>

                {/* Messages Area */}
                <div style={{
                    flex: 1,
                    padding: 16,
                    backgroundColor: '#fafafa',
                }}>
                    {Array.from({ length: 3 }).map((_, index) => (
                        <div key={index} style={{ marginBottom: 24 }}>
                            <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12 }}>
                                <Skeleton.Avatar size="small" />
                                <div style={{ flex: 1 }}>
                                    <Skeleton.Input size="small" style={{ width: 80, marginBottom: 8 }} />
                                    <Skeleton paragraph={{ rows: 2, width: ['100%', '80%'] }} />
                                </div>
                            </div>
                        </div>
                    ))}
                </div>

                {/* Input Area */}
                <div style={{
                    borderTop: '1px solid #f0f0f0',
                    padding: '12px 16px',
                    backgroundColor: '#fff',
                }}>
                    <div style={{ display: 'flex', gap: 8, alignItems: 'flex-end' }}>
                        <Skeleton.Input style={{ flex: 1, height: 66 }} />
                        <Skeleton.Button style={{ height: 66, width: 80 }} />
                    </div>
                </div>
            </Content>

            {/* Right Info Panel Skeleton */}
            <Sider
                width={250}
                theme="light"
                style={{
                    borderLeft: '1px solid #f0f0f0',
                    overflow: 'hidden',
                }}
            >
                <div style={{ padding: 12 }}>
                    <Skeleton.Input style={{ width: 120, marginBottom: 16 }} />

                    {/* Connection info */}
                    <Card size="small" style={{ marginBottom: 16 }}>
                        <Skeleton active paragraph={{ rows: 3 }} />
                    </Card>

                    {/* Quota progress */}
                    <Card size="small" style={{ marginBottom: 16 }}>
                        <Skeleton active paragraph={{ rows: 2 }} />
                    </Card>

                    {/* Schema browser */}
                    <div>
                        <Skeleton.Input style={{ width: 100, marginBottom: 8 }} />
                        {Array.from({ length: 2 }).map((_, index) => (
                            <div key={index} style={{ marginBottom: 12, padding: 8, border: '1px solid #f0f0f0', borderRadius: 4 }}>
                                <Skeleton.Input size="small" style={{ width: '80%', marginBottom: 4 }} />
                                <Skeleton paragraph={{ rows: 2, width: ['60%', '70%'] }} />
                            </div>
                        ))}
                    </div>
                </div>
            </Sider>
        </Layout>
    );
};

export default LayoutSkeleton;