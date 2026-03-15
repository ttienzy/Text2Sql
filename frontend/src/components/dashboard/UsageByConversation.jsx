/**
 * UsageByConversation Component
 * Displays token usage grouped by conversation
 */
import { useState, useMemo } from 'react';
import { 
  Card, 
  Table, 
  Typography, 
  Space, 
  Tag, 
  DatePicker, 
  Input,
  Button,
  Skeleton,
  Empty,
} from 'antd';
import { 
  MessageOutlined, 
  SearchOutlined, 
  ReloadOutlined,
} from '@ant-design/icons';
import { useConversationsQuery } from '../../api/conversations';
import dayjs from 'dayjs';

const { Text } = Typography;
const { RangePicker } = DatePicker;

/**
 * Get tag color based on conversation recency
 */
const getRecencyTag = (createdAt) => {
  if (!createdAt) return <Tag>Unknown</Tag>;
  
  const daysDiff = dayjs().diff(dayjs(createdAt), 'day');
  
  if (daysDiff === 0) return <Tag color="green">Today</Tag>;
  if (daysDiff === 1) return <Tag color="blue">Yesterday</Tag>;
  if (daysDiff <= 7) return <Tag color="cyan">This Week</Tag>;
  if (daysDiff <= 30) return <Tag color="orange">This Month</Tag>;
  return <Tag>Older</Tag>;
};

const UsageByConversation = ({ onConversationClick }) => {
  const [searchText, setSearchText] = useState('');
  const [dateRange, setDateRange] = useState(null);
  const [isRefreshing, setIsRefreshing] = useState(false);

  // Fetch conversations
  const { 
    data: conversations = [], 
    isLoading, 
    refetch 
  } = useConversationsQuery(null, { limit: 100 });

  // Transform data with mock token usage
  // In real app, this would come from useUsageByConversationQuery
  const tableData = useMemo(() => {
    // Use deterministic pseudo-random values based on index
    const getDeterministicValue = (index, min, max) => {
      const seed = (index * 7 + 3) % 100;
      return min + Math.floor((seed / 100) * (max - min));
    };
    
    return conversations.map((conv, index) => ({
      key: conv.id,
      id: conv.id,
      title: conv.title || `Conversation ${index + 1}`,
      createdAt: conv.createdAt,
      updatedAt: conv.updatedAt,
      // Deterministic mock token data - in real app, this comes from API
      inputTokens: getDeterministicValue(index, 1000, 6000),
      outputTokens: getDeterministicValue(index, 2000, 12000),
      totalTokens: 0, // Will be calculated
      messageCount: conv.messageCount || getDeterministicValue(index, 1, 20),
    })).map(conv => ({
      ...conv,
      totalTokens: conv.inputTokens + conv.outputTokens,
    })).sort((a, b) => b.totalTokens - a.totalTokens);
  }, [conversations]);

  // Filter by search text and date range
  const filteredData = useMemo(() => {
    let data = tableData;

    if (searchText) {
      data = data.filter(conv => 
        conv.title.toLowerCase().includes(searchText.toLowerCase())
      );
    }

    if (dateRange && dateRange[0] && dateRange[1]) {
      const [start, end] = dateRange;
      data = data.filter(conv => {
        const convDate = dayjs(conv.createdAt);
        return convDate.isAfter(start.startOf('day')) && convDate.isBefore(end.endOf('day'));
      });
    }

    return data;
  }, [tableData, searchText, dateRange]);

  // Calculate totals
  const totals = useMemo(() => {
    return filteredData.reduce((acc, conv) => ({
      input: acc.input + conv.inputTokens,
      output: acc.output + conv.outputTokens,
      total: acc.total + conv.totalTokens,
    }), { input: 0, output: 0, total: 0 });
  }, [filteredData]);

  const handleRefresh = async () => {
    setIsRefreshing(true);
    await refetch();
    setIsRefreshing(false);
  };

  const handleConversationClick = (record) => {
    if (onConversationClick) {
      onConversationClick(record);
    }
  };

  const columns = [
    {
      title: 'Conversation',
      dataIndex: 'title',
      key: 'title',
      sorter: (a, b) => a.title.localeCompare(b.title),
      render: (text, record) => (
        <Space direction="vertical" size={0}>
          <Text strong style={{ cursor: 'pointer', color: '#1890ff' }} onClick={() => handleConversationClick(record)}>
            {text}
          </Text>
          <Text type="secondary" style={{ fontSize: 11 }}>
            {record.messageCount} messages
          </Text>
        </Space>
      ),
    },
    {
      title: 'Input Tokens',
      dataIndex: 'inputTokens',
      key: 'inputTokens',
      width: 120,
      sorter: (a, b) => a.inputTokens - b.inputTokens,
      render: (value) => value.toLocaleString(),
      align: 'right',
    },
    {
      title: 'Output Tokens',
      dataIndex: 'outputTokens',
      key: 'outputTokens',
      width: 120,
      sorter: (a, b) => a.outputTokens - b.outputTokens,
      render: (value) => value.toLocaleString(),
      align: 'right',
    },
    {
      title: 'Total Tokens',
      dataIndex: 'totalTokens',
      key: 'totalTokens',
      width: 130,
      sorter: (a, b) => a.totalTokens - b.totalTokens,
      render: (value) => (
        <Text strong style={{ color: '#1890ff' }}>
          {value.toLocaleString()}
        </Text>
      ),
      defaultSortOrder: 'descend',
    },
    {
      title: 'Created',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 120,
      sorter: (a, b) => new Date(a.createdAt) - new Date(b.createdAt),
      render: (date) => (
        <Space direction="vertical" size={0}>
          <Text style={{ fontSize: 12 }}>
            {date ? dayjs(date).format('MM/DD/YYYY') : 'N/A'}
          </Text>
          {getRecencyTag(date)}
        </Space>
      ),
    },
  ];

  // Use conditional rendering instead of early returns to fix hook order
  const renderContent = () => {
    if (isLoading) {
      return <Skeleton active paragraph={{ rows: 8 }} />;
    }

    if (!tableData.length) {
      return (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={
            <div>
              <div>No conversations yet</div>
              <Text type="secondary" style={{ fontSize: 12 }}>
                Start chatting to see your usage by conversation
              </Text>
            </div>
          }
        />
      );
    }

    return (
      <>
        {/* Summary Stats */}
        <div style={{ 
          display: 'grid', 
          gridTemplateColumns: 'repeat(3, 1fr)', 
          gap: 16,
          marginBottom: 16,
          padding: 16,
          background: '#fafafa',
          borderRadius: 8,
        }}>
          <div>
            <Text type="secondary" style={{ fontSize: 12 }}>Total Input</Text>
            <div style={{ fontSize: 16, fontWeight: 'bold', color: '#52c41a' }}>
              {totals.input.toLocaleString()}
            </div>
          </div>
          <div>
            <Text type="secondary" style={{ fontSize: 12 }}>Total Output</Text>
            <div style={{ fontSize: 16, fontWeight: 'bold', color: '#1890ff' }}>
              {totals.output.toLocaleString()}
            </div>
          </div>
          <div>
            <Text type="secondary" style={{ fontSize: 12 }}>Total Tokens</Text>
            <div style={{ fontSize: 16, fontWeight: 'bold', color: '#722ed1' }}>
              {totals.total.toLocaleString()}
            </div>
          </div>
        </div>

        <Table
          columns={columns}
          dataSource={filteredData}
          pagination={{
            pageSize: 10,
            showSizeChanger: true,
            showTotal: (total, range) => `${range[0]}-${range[1]} of ${total} conversations`,
          }}
          size="small"
          onRow={(record) => ({
            onClick: () => handleConversationClick(record),
            style: { cursor: 'pointer' },
          })}
        />
      </>
    );
  };

  return (
    <Card
      title={
        <Space>
          <MessageOutlined />
          <span>Usage by Conversation</span>
        </Space>
      }
      extra={
        <Space>
          <Input
            placeholder="Search conversations"
            prefix={<SearchOutlined />}
            value={searchText}
            onChange={(e) => setSearchText(e.target.value)}
            style={{ width: 200 }}
            allowClear
          />
          <RangePicker 
            value={dateRange}
            onChange={setDateRange}
            placeholder={['Start', 'End']}
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
      {renderContent()}
    </Card>
  );
};

export default UsageByConversation;
