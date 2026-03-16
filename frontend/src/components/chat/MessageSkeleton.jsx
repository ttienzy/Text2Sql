/**
 * MessageSkeleton - Loading state for individual messages
 */
import { Skeleton, Space } from 'antd';

const MessageSkeleton = ({ isUser = false }) => {
    return (
        <div style={{
            display: 'flex',
            justifyContent: isUser ? 'flex-end' : 'flex-start',
            marginBottom: 16,
        }}>
            <div style={{
                maxWidth: '70%',
                padding: 12,
                borderRadius: 8,
                backgroundColor: isUser ? '#e6f7ff' : '#f6f6f6',
                border: '1px solid #f0f0f0',
            }}>
                <Space direction="vertical" size="small" style={{ width: '100%' }}>
                    <Skeleton.Input size="small" style={{ width: 200 }} active />
                    <Skeleton paragraph={{ rows: 2, width: ['100%', '80%'] }} active />
                    {!isUser && (
                        <div style={{ marginTop: 8 }}>
                            <Skeleton.Input size="small" style={{ width: 150 }} active />
                        </div>
                    )}
                </Space>
            </div>
        </div>
    );
};

export default MessageSkeleton;