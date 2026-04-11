import React from 'react';
import { 
  Typography, 
  Space, 
  Progress, 
  Tag,
} from 'antd';
import { 
  ThunderboltOutlined, 
  ClockCircleOutlined,
} from '@ant-design/icons';

const { Text } = Typography;

const TokenInfo = ({ 
  inputTokens = 0, 
  outputTokens = 0, 
  totalTokens = 0, 
  model = 'gpt-4',
  dailyLimit = 0,
  usedToday = 0,
}) => {
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
      <Space>
        <ThunderboltOutlined style={{ color: '#faad14' }} />
        <Text strong style={{ fontSize: 12 }}>Tokens</Text>
        {model && <Tag style={{ fontSize: 10 }}>{model}</Tag>}
      </Space>
      
      <div style={{ display: 'flex', gap: 16, marginTop: 8 }}>
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
      </div>
    </div>
  );
};

export default TokenInfo;
