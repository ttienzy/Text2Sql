import React from 'react';
import { 
  Typography, 
  Space, 
  Progress, 
  Tooltip,
  Tag,
} from 'antd';
import { 
  ThunderboltOutlined, 
  DollarOutlined,
  ClockCircleOutlined,
} from '@ant-design/icons';

const { Text } = Typography;

/**
 * TokenInfo - Component for displaying token usage information
 * @param {Object} props
 * @param {number} props.inputTokens - Number of input tokens used
 * @param {number} props.outputTokens - Number of output tokens used
 * @param {number} props.totalTokens - Total tokens used
 * @param {number} props.cost - Estimated cost in USD
 * @param {string} props.model - Model name used
 * @param {number} props.dailyLimit - Daily token limit (optional, for quota display)
 * @param {number} props.usedToday - Tokens used today (optional, for quota display)
 */
const TokenInfo = ({ 
  inputTokens = 0, 
  outputTokens = 0, 
  totalTokens = 0, 
  cost = 0, 
  model = 'gpt-4',
  dailyLimit = 0,
  usedToday = 0,
}) => {
  // Calculate cost in VND (approximate: 1 USD = 24000 VND)
  const costInVND = cost * 24000;
  
  // Calculate usage percentage if daily limit is provided
  const usagePercentage = dailyLimit > 0 
    ? Math.min((usedToday / dailyLimit) * 100, 100) 
    : 0;
  
  return (
    <div 
      style={{ 
        marginTop: 12,
        padding: '10px 12px',
        backgroundColor: '#fafafa',
        borderRadius: 4,
        border: '1px solid #f0f0f0',
      }}
    >
      {/* Header */}
      <div style={{ marginBottom: 8 }}>
        <Space>
          <ThunderboltOutlined style={{ color: '#faad14' }} />
          <Text strong style={{ fontSize: 12 }}>Token Usage</Text>
          {model && <Tag style={{ fontSize: 10 }}>{model}</Tag>}
        </Space>
      </div>
      
      {/* Token breakdown */}
      <div style={{ display: 'flex', gap: 16, marginBottom: 8 }}>
        <div>
          <Text type="secondary" style={{ fontSize: 11 }}>Input</Text>
          <div>
            <Text strong style={{ fontSize: 13 }}>
              {inputTokens.toLocaleString()}
            </Text>
          </div>
        </div>
        
        <div>
          <Text type="secondary" style={{ fontSize: 11 }}>Output</Text>
          <div>
            <Text strong style={{ fontSize: 13 }}>
              {outputTokens.toLocaleString()}
            </Text>
          </div>
        </div>
        
        <div>
          <Text type="secondary" style={{ fontSize: 11 }}>Total</Text>
          <div>
            <Text strong style={{ fontSize: 13, color: '#1890ff' }}>
              {totalTokens.toLocaleString()}
            </Text>
          </div>
        </div>
        
        {cost > 0 && (
          <div>
            <Text type="secondary" style={{ fontSize: 11 }}>Cost</Text>
            <div>
              <Space>
                <DollarOutlined style={{ color: '#52c41a' }} />
                <Text strong style={{ fontSize: 13, color: '#52c41a' }}>
                  ~{costInVND.toLocaleString()} VND
                </Text>
              </Space>
            </div>
          </div>
        )}
      </div>
      
      {/* Progress bar for daily quota */}
      {dailyLimit > 0 && (
        <div>
          <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 4 }}>
            <Text type="secondary" style={{ fontSize: 11 }}>
              <ClockCircleOutlined style={{ marginRight: 4 }} />
              Daily Quota
            </Text>
            <Text type="secondary" style={{ fontSize: 11 }}>
              {usedToday.toLocaleString()} / {dailyLimit.toLocaleString()}
            </Text>
          </div>
          <Progress 
            percent={Math.round(usagePercentage)} 
            size="small"
            status={usagePercentage > 90 ? 'exception' : usagePercentage > 70 ? 'normal' : 'success'}
            showInfo={false}
            strokeColor={usagePercentage > 90 ? '#ff4d4f' : usagePercentage > 70 ? '#faad14' : '#52c41a'}
          />
        </div>
      )}
    </div>
  );
};

export default TokenInfo;
