/**
 * QuotaProgress Component
 * Displays token quota with progress bar and color coding
 */
import { useState } from 'react';
import { 
  Progress, 
  Typography, 
  Space, 
  Button, 
  Tooltip, 
  Card,
  Skeleton,
} from 'antd';
import { 
  ThunderboltOutlined, 
  ReloadOutlined, 
  WarningOutlined,
  CheckCircleOutlined,
  CloseCircleOutlined,
} from '@ant-design/icons';
import { useQuotaQuery } from '../../api/observability';

const { Text, Title } = Typography;

/**
 * Get color based on usage percentage
 * @param {number} percentage - Usage percentage (0-100)
 * @returns {string} - Color code
 */
const getUsageColor = (percentage) => {
  if (percentage >= 100) return '#ff4d4f'; // Red - exceeded
  if (percentage >= 80) return '#faad14';  // Yellow - warning
  return '#52c41a'; // Green - OK
};

/**
 * Get status text based on usage percentage
 * @param {number} percentage - Usage percentage
 * @returns {string} - Status text
 */
const getStatusText = (percentage) => {
  if (percentage >= 100) return 'Quota Exceeded';
  if (percentage >= 80) return 'Warning: Low Quota';
  return 'Normal';
};

/**
 * Get status type for Ant Design
 * @param {number} percentage - Usage percentage
 * @returns {string} - Status type
 */
const getStatusType = (percentage) => {
  if (percentage >= 100) return 'exception';
  if (percentage >= 80) return 'normal';
  return 'success';
};

const QuotaProgress = ({ compact = false, onUpgradeClick }) => {
  const [isRefreshing, setIsRefreshing] = useState(false);
  
  const { 
    data: quota, 
    isLoading, 
    error, 
    refetch,
    isFetching 
  } = useQuotaQuery({
    queryOptions: {
      retry: 2,
      retryDelay: 1000,
    }
  });

  const handleRefresh = async () => {
    setIsRefreshing(true);
    await refetch();
    setIsRefreshing(false);
  };

  // Calculate percentage
  const percentage = quota?.usagePercentage ?? 
    (quota?.dailyLimit ? Math.round((quota.usedToday / quota.dailyLimit) * 100) : 0);
  
  const usedTokens = quota?.usedToday ?? 0;
  const totalTokens = quota?.dailyLimit ?? 0;
  const remainingTokens = quota?.remaining ?? 0;
  const isUnlimited = quota?.isUnlimited ?? false;

  // Loading state
  if (isLoading) {
    if (compact) {
      return <Skeleton.Input active size="small" style={{ width: 120 }} />;
    }
    return (
      <Card size="small">
        <Skeleton active paragraph={{ rows: 2 }} />
      </Card>
    );
  }

  // Error state
  if (error) {
    return (
      <Card size="small">
        <Space direction="vertical" style={{ width: '100%' }}>
          <Text type="danger">
            <CloseCircleOutlined /> Failed to load quota
          </Text>
          <Button 
            size="small" 
            icon={<ReloadOutlined />} 
            onClick={() => refetch()}
          >
            Retry
          </Button>
        </Space>
      </Card>
    );
  }

  // Unlimited quota
  if (isUnlimited) {
    return (
      <Card size="small">
        <Space direction="vertical" style={{ width: '100%' }}>
          <Space>
            <CheckCircleOutlined style={{ color: '#52c41a' }} />
            <Text strong>Unlimited Quota</Text>
          </Space>
          <Text type="secondary">You have unlimited access to all features</Text>
        </Space>
      </Card>
    );
  }

  // Compact view (for InfoPanel)
  if (compact) {
    return (
      <Space direction="vertical" style={{ width: '100%' }} size="small">
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <Space>
            <ThunderboltOutlined style={{ color: getUsageColor(percentage) }} />
            <Text strong style={{ fontSize: 13 }}>Token Quota</Text>
          </Space>
          <Tooltip title="Refresh quota">
            <Button 
              type="text" 
              size="small" 
              icon={<ReloadOutlined spin={isFetching} />} 
              onClick={handleRefresh}
              loading={isRefreshing}
            />
          </Tooltip>
        </div>
        
        <Progress 
          percent={Math.min(percentage, 100)} 
          size="small"
          status={getStatusType(percentage)}
          strokeColor={getUsageColor(percentage)}
          showInfo={false}
        />
        
        <div style={{ display: 'flex', justifyContent: 'space-between' }}>
          <Text type="secondary" style={{ fontSize: 11 }}>
            {usedTokens.toLocaleString()} / {totalTokens.toLocaleString()}
          </Text>
          <Text type="secondary" style={{ fontSize: 11 }}>
            {remainingTokens.toLocaleString()} remaining
          </Text>
        </div>
        
        {/* Warning Alert */}
        {percentage >= 80 && percentage < 100 && (
          <div style={{ 
            background: '#fffbe6', 
            padding: '8px 12px', 
            borderRadius: 4,
            border: '1px solid #ffe58f'
          }}>
            <Space>
              <WarningOutlined style={{ color: '#faad14' }} />
              <Text style={{ fontSize: 12, color: '#d48806' }}>
                {percentage}% used - Consider upgrading
              </Text>
            </Space>
          </div>
        )}
        
        {/* Exceeded Alert */}
        {percentage >= 100 && (
          <div style={{ 
            background: '#fff1f0', 
            padding: '8px 12px', 
            borderRadius: 4,
            border: '1px solid #ffa39e'
          }}>
            <Space direction="vertical" size={0}>
              <Space>
                <CloseCircleOutlined style={{ color: '#ff4d4f' }} />
                <Text strong style={{ color: '#ff4d4f', fontSize: 12 }}>
                  Quota Exceeded
                </Text>
              </Space>
              {onUpgradeClick && (
                <Button 
                  type="link" 
                  size="small" 
                  onClick={onUpgradeClick}
                  style={{ padding: 0, height: 'auto' }}
                >
                  Upgrade now →
                </Button>
              )}
            </Space>
          </div>
        )}
      </Space>
    );
  }

  // Full view
  return (
    <Card 
      title={
        <Space>
          <ThunderboltOutlined />
          <span>Token Quota</span>
        </Space>
      }
      extra={
        <Tooltip title="Refresh quota">
          <Button 
            type="text" 
            icon={<ReloadOutlined spin={isFetching} />} 
            onClick={handleRefresh}
            loading={isRefreshing}
          />
        </Tooltip>
      }
    >
      <Space direction="vertical" style={{ width: '100%' }} size="middle">
        {/* Main Progress */}
        <div style={{ textAlign: 'center', padding: '16px 0' }}>
          <Progress 
            type="circle"
            percent={Math.min(percentage, 100)}
            size={120}
            status={getStatusType(percentage)}
            strokeColor={getUsageColor(percentage)}
            format={(percent) => (
              <div>
                <div style={{ fontSize: 24, fontWeight: 'bold', color: getUsageColor(percent) }}>
                  {percent}%
                </div>
                <div style={{ fontSize: 11, color: '#8c8c8c' }}>Used</div>
              </div>
            )}
          />
        </div>
        
        {/* Stats */}
        <div style={{ 
          display: 'grid', 
          gridTemplateColumns: 'repeat(3, 1fr)', 
          gap: 16,
          textAlign: 'center'
        }}>
          <div>
            <Text type="secondary" style={{ fontSize: 12 }}>Used Today</Text>
            <div style={{ fontSize: 18, fontWeight: 'bold', color: getUsageColor(percentage) }}>
              {usedTokens.toLocaleString()}
            </div>
          </div>
          <div>
            <Text type="secondary" style={{ fontSize: 12 }}>Total Limit</Text>
            <div style={{ fontSize: 18, fontWeight: 'bold' }}>
              {totalTokens.toLocaleString()}
            </div>
          </div>
          <div>
            <Text type="secondary" style={{ fontSize: 12 }}>Remaining</Text>
            <div style={{ fontSize: 18, fontWeight: 'bold', color: '#52c41a' }}>
              {remainingTokens.toLocaleString()}
            </div>
          </div>
        </div>
        
        {/* Reset Time */}
        {quota?.resetAt && (
          <div style={{ textAlign: 'center', borderTop: '1px solid #f0f0f0', paddingTop: 16 }}>
            <Text type="secondary" style={{ fontSize: 12 }}>
              Quota resets at: {new Date(quota.resetAt).toLocaleString()}
            </Text>
          </div>
        )}
        
        {/* Warning Alert */}
        {percentage >= 80 && percentage < 100 && (
          <div style={{ 
            background: '#fffbe6', 
            padding: 12, 
            borderRadius: 4,
            border: '1px solid #ffe58f'
          }}>
            <Space>
              <WarningOutlined style={{ color: '#faad14' }} />
              <Text strong style={{ color: '#d48806' }}>
                You've used 80% of your quota
              </Text>
            </Space>
            <div style={{ marginTop: 4 }}>
              <Text style={{ fontSize: 12, color: '#d48806' }}>
                Consider upgrading to continue using the service without interruption.
              </Text>
            </div>
          </div>
        )}
        
        {/* Exceeded Alert */}
        {percentage >= 100 && (
          <div style={{ 
            background: '#fff1f0', 
            padding: 12, 
            borderRadius: 4,
            border: '1px solid #ffa39e'
          }}>
            <Space direction="vertical" size="small">
              <Space>
                <CloseCircleOutlined style={{ color: '#ff4d4f' }} />
                <Text strong style={{ color: '#ff4d4f' }}>
                  Daily Quota Exceeded
                </Text>
              </Space>
              <Text style={{ color: '#ff4d4f' }}>
                You've used all your tokens for today. Your quota will reset at midnight UTC.
              </Text>
              {onUpgradeClick && (
                <Button type="primary" danger onClick={onUpgradeClick}>
                  Upgrade Now
                </Button>
              )}
            </Space>
          </div>
        )}
      </Space>
    </Card>
  );
};

export default QuotaProgress;
