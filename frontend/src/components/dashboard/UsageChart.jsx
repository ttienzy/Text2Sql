/**
 * UsageChart Component
 * Displays token usage over time and by model using Recharts
 */
import { useState, useMemo } from 'react';
import { 
  Card, 
  DatePicker, 
  Space, 
  Typography, 
  Select, 
  Skeleton, 
  Empty, 
  Button,
  Tabs,
} from 'antd';
import { 
  LineChartOutlined, 
  BarChartOutlined, 
  ReloadOutlined,
} from '@ant-design/icons';
import { 
  LineChart, 
  Line, 
  XAxis, 
  YAxis, 
  CartesianGrid, 
  Tooltip, 
  ResponsiveContainer,
  BarChart,
  Bar,
  Legend,
  PieChart,
  Pie,
  Cell,
} from 'recharts';
import { useQuotaQuery, useUsageHistoryQuery } from '../../api/observability';
import dayjs from 'dayjs';

const { Text, Title } = Typography;
const { RangePicker } = DatePicker;

// Color palette for charts
const COLORS = ['#1890ff', '#52c41a', '#faad14', '#f5222d', '#722ed1', '#13c2c2'];

/**
 * Format date for X-axis
 */
const formatDate = (dateStr) => {
  return dayjs(dateStr).format('MM/DD');
};

/**
 * Format large numbers with K/M suffix
 */
const formatNumber = (value) => {
  if (value >= 1000000) return `${(value / 1000000).toFixed(1)}M`;
  if (value >= 1000) return `${(value / 1000).toFixed(1)}K`;
  return value.toString();
};

const UsageChart = () => {
  const [dateRange, setDateRange] = useState([
    dayjs().subtract(7, 'day'),
    dayjs()
  ]);
  const [chartType, setChartType] = useState('line');
  const [isRefreshing, setIsRefreshing] = useState(false);

  // Fetch quota data
  const { 
    data: quota, 
    isLoading: isQuotaLoading,
    refetch: refetchQuota 
  } = useQuotaQuery();

  // Fetch usage history
  const { 
    data: usageData, 
    isLoading: isHistoryLoading,
    refetch: refetchHistory 
  } = useUsageHistoryQuery({
    from: dateRange[0]?.toISOString(),
    to: dateRange[1]?.toISOString(),
  });

  // Transform data for line/bar chart
  const chartData = useMemo(() => {
    if (!usageData || !Array.isArray(usageData)) {
      // Generate sample data based on quota if no usage data
      const days = [];
      const usedToday = quota?.usedToday || 10000;
      for (let i = 6; i >= 0; i--) {
        const date = dayjs().subtract(i, 'day').format('YYYY-MM-DD');
        // Fixed sample data - in real app, this would come from API
        const dailyUsage = Math.floor(usedToday / 7 * (0.5 + (i * 0.1)));
        days.push({
          date,
          tokens: dailyUsage,
          formattedDate: dayjs(date).format('MM/DD'),
        });
      }
      return days;
    }
    
    return usageData.map(item => ({
      date: item.date || item.timestamp,
      tokens: item.tokens || item.totalTokens || 0,
      formattedDate: formatDate(item.date || item.timestamp),
    }));
  }, [usageData, quota]);

  // Model usage data (mock data - would come from API)
  const modelData = useMemo(() => [
    { name: 'GPT-4', tokens: 45000, color: COLORS[0] },
    { name: 'GPT-3.5', tokens: 25000, color: COLORS[1] },
    { name: 'Gemini', tokens: 15000, color: COLORS[2] },
  ], []);

  const handleRefresh = async () => {
    setIsRefreshing(true);
    await Promise.all([refetchQuota(), refetchHistory()]);
    setIsRefreshing(false);
  };

  const handleDateChange = (dates) => {
    if (dates) {
      setDateRange(dates);
    }
  };

  const isLoading = isQuotaLoading || isHistoryLoading;

  // Loading state
  if (isLoading && !chartData.length) {
    return (
      <Card>
        <Skeleton active paragraph={{ rows: 6 }} />
      </Card>
    );
  }

  // Empty state
  if (!isLoading && !chartData.length) {
    return (
      <Card>
        <Empty 
          description="No usage data available"
          image={Empty.PRESENTED_IMAGE_SIMPLE}
        >
          <Button type="primary" onClick={handleRefresh}>
            Refresh
          </Button>
        </Empty>
      </Card>
    );
  }

  const tabItems = [
    {
      key: 'line',
      label: <span><LineChartOutlined /> Timeline</span>,
      children: (
        <ResponsiveContainer width="100%" height={300}>
          <LineChart data={chartData} margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
            <XAxis 
              dataKey="formattedDate" 
              stroke="#8c8c8c"
              tick={{ fontSize: 12 }}
            />
            <YAxis 
              stroke="#8c8c8c"
              tick={{ fontSize: 12 }}
              tickFormatter={formatNumber}
            />
            <Tooltip 
              formatter={(value) => [value.toLocaleString(), 'Tokens']}
              labelFormatter={(label) => `Date: ${label}`}
              contentStyle={{ 
                borderRadius: 8, 
                border: '1px solid #f0f0f0',
                boxShadow: '0 2px 8px rgba(0,0,0,0.1)'
              }}
            />
            <Line 
              type="monotone" 
              dataKey="tokens" 
              stroke="#1890ff" 
              strokeWidth={2}
              dot={{ fill: '#1890ff', strokeWidth: 2, r: 4 }}
              activeDot={{ r: 6, fill: '#1890ff' }}
              name="Tokens Used"
            />
          </LineChart>
        </ResponsiveContainer>
      ),
    },
    {
      key: 'bar',
      label: <span><BarChartOutlined /> Daily</span>,
      children: (
        <ResponsiveContainer width="100%" height={300}>
          <BarChart data={chartData} margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="#f0f0f0" />
            <XAxis 
              dataKey="formattedDate" 
              stroke="#8c8c8c"
              tick={{ fontSize: 12 }}
            />
            <YAxis 
              stroke="#8c8c8c"
              tick={{ fontSize: 12 }}
              tickFormatter={formatNumber}
            />
            <Tooltip 
              formatter={(value) => [value.toLocaleString(), 'Tokens']}
              labelFormatter={(label) => `Date: ${label}`}
              contentStyle={{ 
                borderRadius: 8, 
                border: '1px solid #f0f0f0',
                boxShadow: '0 2px 8px rgba(0,0,0,0.1)'
              }}
            />
            <Bar 
              dataKey="tokens" 
              fill="#1890ff" 
              radius={[4, 4, 0, 0]}
              name="Tokens Used"
            />
          </BarChart>
        </ResponsiveContainer>
      ),
    },
  ];

  return (
    <Card
      title={
        <Space>
          <LineChartOutlined />
          <span>Usage History</span>
        </Space>
      }
      extra={
        <Space>
          <RangePicker 
            value={dateRange}
            onChange={handleDateChange}
            allowClear={false}
            disabledDate={(current) => current && current > dayjs().endOf('day')}
          />
          <Button 
            icon={<ReloadOutlined spin={isRefreshing} />} 
            onClick={handleRefresh}
            loading={isRefreshing}
          >
            Refresh
          </Button>
        </Space>
      }
    >
      <Tabs 
        activeKey={chartType} 
        onChange={setChartType}
        items={tabItems}
      />
      
      {/* Summary Stats */}
      <div style={{ 
        display: 'grid', 
        gridTemplateColumns: 'repeat(4, 1fr)', 
        gap: 16,
        marginTop: 16,
        paddingTop: 16,
        borderTop: '1px solid #f0f0f0'
      }}>
        <div>
          <Text type="secondary" style={{ fontSize: 12 }}>Total Used</Text>
          <div style={{ fontSize: 18, fontWeight: 'bold' }}>
            {chartData.reduce((sum, d) => sum + d.tokens, 0).toLocaleString()}
          </div>
        </div>
        <div>
          <Text type="secondary" style={{ fontSize: 12 }}>Average/Day</Text>
          <div style={{ fontSize: 18, fontWeight: 'bold' }}>
            {Math.round(chartData.reduce((sum, d) => sum + d.tokens, 0) / chartData.length).toLocaleString()}
          </div>
        </div>
        <div>
          <Text type="secondary" style={{ fontSize: 12 }}>Peak Day</Text>
          <div style={{ fontSize: 18, fontWeight: 'bold' }}>
            {Math.max(...chartData.map(d => d.tokens)).toLocaleString()}
          </div>
        </div>
        <div>
          <Text type="secondary" style={{ fontSize: 12 }}>Days Tracked</Text>
          <div style={{ fontSize: 18, fontWeight: 'bold' }}>
            {chartData.length}
          </div>
        </div>
      </div>

      {/* Model Usage Pie Chart */}
      <div style={{ marginTop: 24 }}>
        <Text strong style={{ display: 'block', marginBottom: 16 }}>Usage by Model</Text>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
          <ResponsiveContainer width="50%" height={200}>
            <PieChart>
              <Pie
                data={modelData}
                cx="50%"
                cy="50%"
                innerRadius={40}
                outerRadius={80}
                paddingAngle={2}
                dataKey="tokens"
                nameKey="name"
                label={({ name, percent }) => `${name} ${(percent * 100).toFixed(0)}%`}
                labelLine={false}
              >
                {modelData.map((entry, index) => (
                  <Cell key={`cell-${index}`} fill={entry.color || COLORS[index % COLORS.length]} />
                ))}
              </Pie>
              <Tooltip 
                formatter={(value) => [value.toLocaleString(), 'Tokens']}
                contentStyle={{ 
                  borderRadius: 8, 
                  border: '1px solid #f0f0f0',
                }}
              />
            </PieChart>
          </ResponsiveContainer>
          
          {/* Legend */}
          <div style={{ marginLeft: 24 }}>
            {modelData.map((model, index) => (
              <div key={model.name} style={{ display: 'flex', alignItems: 'center', marginBottom: 8 }}>
                <div style={{ 
                  width: 12, 
                  height: 12, 
                  borderRadius: 2, 
                  backgroundColor: model.color || COLORS[index % COLORS.length],
                  marginRight: 8
                }} />
                <Text style={{ marginRight: 8 }}>{model.name}</Text>
                <Text type="secondary">{model.tokens.toLocaleString()}</Text>
              </div>
            ))}
          </div>
        </div>
      </div>
    </Card>
  );
};

export default UsageChart;
