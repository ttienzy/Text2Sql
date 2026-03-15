/**
 * UsageByModel Component
 * Displays token usage statistics grouped by AI model
 */
import { useMemo } from 'react';
import { 
  Card, 
  Typography, 
  Space, 
  Statistic, 
  Row, 
  Col,
  Progress,
  Skeleton,
  Empty,
  Button,
} from 'antd';
import { 
  RobotOutlined, 
  ThunderboltOutlined,
  ReloadOutlined,
  ArrowUpOutlined,
  ArrowDownOutlined,
} from '@ant-design/icons';
import { 
  BarChart, 
  Bar, 
  XAxis, 
  YAxis, 
  CartesianGrid, 
  Tooltip, 
  ResponsiveContainer,
  Cell,
} from 'recharts';
import { useUsageByModelQuery } from '../../api/observability';

const { Text, Title } = Typography;

// Color palette for different models
const MODEL_COLORS = {
  'GPT-4': '#10a37f',
  'GPT-4o': '#10a37f',
  'GPT-4 Turbo': '#10a37f',
  'GPT-3.5': '#6ee7b7',
  'GPT-3.5 Turbo': '#6ee7b7',
  'Gemini': '#4285f4',
  'Claude': '#d97706',
  'Default': '#1890ff',
};

/**
 * Get color for a model
 */
const getModelColor = (modelName) => {
  if (!modelName) return MODEL_COLORS.Default;
  const key = Object.keys(MODEL_COLORS).find(k => 
    modelName.toLowerCase().includes(k.toLowerCase())
  );
  return MODEL_COLORS[key] || MODEL_COLORS.Default;
};

const UsageByModel = () => {
  // Fetch usage by model
  const { 
    data: modelData, 
    isLoading: isModelLoading,
    refetch: refetchModel 
  } = useUsageByModelQuery();

  // Calculate mock data if API doesn't return data
  const processedData = useMemo(() => {
    if (modelData && Array.isArray(modelData) && modelData.length > 0) {
      return modelData.map(model => ({
        name: model.model || model.name,
        tokens: model.tokens || model.totalTokens || 0,
        requests: model.requests || 0,
        avgTokensPerRequest: model.avgTokensPerRequest || 0,
        cost: model.cost || 0,
        color: getModelColor(model.model || model.name),
      }));
    }
    
    // Default mock data
    return [
      { name: 'GPT-4', tokens: 45000, requests: 120, avgTokensPerRequest: 375, cost: 0.225, color: MODEL_COLORS['GPT-4'] },
      { name: 'GPT-3.5', tokens: 25000, requests: 200, avgTokensPerRequest: 125, cost: 0.0375, color: MODEL_COLORS['GPT-3.5'] },
      { name: 'Gemini', tokens: 15000, requests: 80, avgTokensPerRequest: 187.5, cost: 0.0525, color: MODEL_COLORS['Gemini'] },
    ];
  }, [modelData]);

  // Calculate totals
  const totals = useMemo(() => {
    return processedData.reduce((acc, model) => ({
      tokens: acc.tokens + model.tokens,
      requests: acc.requests + model.requests,
      cost: acc.cost + model.cost,
    }), { tokens: 0, requests: 0, cost: 0 });
  }, [processedData]);

  // Calculate percentages
  const dataWithPercentage = useMemo(() => {
    const totalTokens = totals.tokens || 1;
    return processedData.map(model => ({
      ...model,
      percentage: (model.tokens / totalTokens * 100).toFixed(1),
    }));
  }, [processedData, totals]);

  // Find top model
  const topModel = useMemo(() => {
    return processedData.reduce((max, model) => 
      model.tokens > max.tokens ? model : max
    , processedData[0] || { name: 'N/A', tokens: 0 });
  }, [processedData]);

  const handleRefresh = async () => {
    await refetchModel();
  };

  const isLoading = isModelLoading;

  // Loading state
  if (isLoading && !processedData.length) {
    return (
      <Card>
        <Skeleton active paragraph={{ rows: 8 }} />
      </Card>
    );
  }

  // Empty state
  if (!isLoading && !processedData.length) {
    return (
      <Card
        title={
          <Space>
            <RobotOutlined />
            <span>Usage by Model</span>
          </Space>
        }
      >
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description="No model usage data available"
        >
          <Button onClick={handleRefresh}>Refresh</Button>
        </Empty>
      </Card>
    );
  }

  return (
    <Card
      title={
        <Space>
          <RobotOutlined />
          <span>Usage by Model</span>
        </Space>
      }
      extra={
        <Button 
          icon={<ReloadOutlined />} 
          onClick={handleRefresh}
        >
          Refresh
        </Button>
      }
    >
      {/* Summary Stats */}
      <Row gutter={[16, 16]} style={{ marginBottom: 24 }}>
        <Col xs={24} sm={8}>
          <Card size="small" style={{ background: '#f0f5ff' }}>
            <Statistic
              title="Total Tokens"
              value={totals.tokens}
              suffix={
                <span style={{ fontSize: 14, color: '#1890ff' }}>
                  tokens
                </span>
              }
            />
          </Card>
        </Col>
        <Col xs={24} sm={8}>
          <Card size="small" style={{ background: '#f6ffed' }}>
            <Statistic
              title="Total Requests"
              value={totals.requests}
              suffix={
                <span style={{ fontSize: 14, color: '#52c41a' }}>
                  requests
                </span>
              }
            />
          </Card>
        </Col>
        <Col xs={24} sm={8}>
          <Card size="small" style={{ background: '#fffbe6' }}>
            <Statistic
              title="Estimated Cost"
              value={totals.cost}
              precision={4}
              prefix="$"
              suffix={
                <span style={{ fontSize: 14, color: '#faad14' }}>
                  USD
                </span>
              }
            />
          </Card>
        </Col>
      </Row>

      {/* Bar Chart */}
      <div style={{ marginBottom: 24 }}>
        <Text strong style={{ display: 'block', marginBottom: 16 }}>Token Distribution</Text>
        <ResponsiveContainer width="100%" height={250}>
          <BarChart data={dataWithPercentage} layout="vertical" margin={{ top: 5, right: 30, left: 80, bottom: 5 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
            <XAxis 
              type="number" 
              stroke="#8c8c8c"
              tickFormatter={(value) => value.toLocaleString()}
            />
            <YAxis 
              dataKey="name" 
              type="category" 
              stroke="#8c8c8c"
              width={70}
            />
            <Tooltip 
              formatter={(value, name) => [
                value.toLocaleString(), 
                name === 'tokens' ? 'Tokens' : name
              ]}
              contentStyle={{ 
                borderRadius: 8, 
                border: '1px solid #f0f0f0',
              }}
            />
            <Bar dataKey="tokens" radius={[0, 4, 4, 0]} name="Tokens">
              {dataWithPercentage.map((entry, index) => (
                <Cell key={`cell-${index}`} fill={entry.color} />
              ))}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>

      {/* Model Breakdown */}
      <Text strong style={{ display: 'block', marginBottom: 16 }}>Model Breakdown</Text>
      {dataWithPercentage.map((model) => (
        <div 
          key={model.name} 
          style={{ 
            marginBottom: 16,
            padding: 16,
            background: '#fafafa',
            borderRadius: 8,
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
            <Space>
              <div style={{ 
                width: 12, 
                height: 12, 
                borderRadius: 2, 
                backgroundColor: model.color 
              }} />
              <Text strong>{model.name}</Text>
              {model.name === topModel.name && (
                <Text type="success" style={{ fontSize: 12 }}>
                  <ArrowUpOutlined /> Top Model
                </Text>
              )}
            </Space>
            <Text strong style={{ fontSize: 16, color: model.color }}>
              {model.tokens.toLocaleString()} tokens
            </Text>
          </div>
          
          <Progress 
            percent={parseFloat(model.percentage)} 
            strokeColor={model.color}
            showInfo={false}
            size="small"
          />
          
          <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 8 }}>
            <Text type="secondary" style={{ fontSize: 12 }}>
              {model.requests} requests
            </Text>
            <Text type="secondary" style={{ fontSize: 12 }}>
              {model.avgTokensPerRequest.toLocaleString()} avg tokens/request
            </Text>
            <Text type="secondary" style={{ fontSize: 12 }}>
              ${model.cost.toFixed(4)} cost
            </Text>
          </div>
        </div>
      ))}
    </Card>
  );
};

export default UsageByModel;
