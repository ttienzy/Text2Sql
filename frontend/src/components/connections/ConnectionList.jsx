/**
 * ConnectionList - Grid/List view for displaying connections
 */
import { useState, useMemo } from 'react';
import {
  Row,
  Col,
  Input,
  Select,
  Space,
  Empty,
  Spin,
  Button,
  Typography,
  Segmented,
  Card,
  Table,
  Tag,
} from 'antd';
import {
  PlusOutlined,
  SearchOutlined,
  AppstoreOutlined,
  UnorderedListOutlined,
  DatabaseOutlined,
} from '@ant-design/icons';
import ConnectionCard from './ConnectionCard';

const { Title, Text } = Typography;
const { Option } = Select;

/**
 * Provider filter options
 */
const PROVIDER_FILTERS = [
  { value: 'all', label: 'All Providers' },
  { value: 'SqlServer', label: 'SQL Server' },
  { value: 'PostgreSql', label: 'PostgreSQL' },
  { value: 'MySql', label: 'MySQL' },
  { value: 'Sqlite', label: 'SQLite' },
];

/**
 * ConnectionList Component
 * @param {Object} props
 * @param {Array} props.connections - List of connections
 * @param {boolean} props.isLoading - Loading state
 * @param {Object} props.activeConnection - Currently active connection
 * @param {Function} props.onConnect - Connect to a connection
 * @param {Function} props.onEdit - Edit a connection
 * @param {Function} props.onDelete - Delete a connection
 * @param {Function} props.onTest - Test a connection
 * @param {Function} props.onSync - Sync schema for a connection
 * @param {Function} props.onAddNew - Add new connection handler
 * @param {boolean} props.isTesting - Test in progress
 * @param {boolean} props.isSyncing - Sync in progress
 */
const ConnectionList = ({
  connections = [],
  isLoading = false,
  activeConnection,
  onConnect,
  onEdit,
  onDelete,
  onTest,
  onSync,
  onAddNew,
  isTesting = false,
  isSyncing = false,
}) => {
  const [viewMode, setViewMode] = useState('grid');
  const [searchText, setSearchText] = useState('');
  const [providerFilter, setProviderFilter] = useState('all');

  // Filter connections based on search and provider
  const filteredConnections = useMemo(() => {
    return connections.filter((conn) => {
      // Search filter
      const searchLower = searchText.toLowerCase();
      const matchesSearch =
        !searchText ||
        conn.name.toLowerCase().includes(searchLower) ||
        conn.host.toLowerCase().includes(searchLower) ||
        conn.database.toLowerCase().includes(searchLower);

      // Provider filter
      const matchesProvider =
        providerFilter === 'all' || conn.provider === providerFilter;

      return matchesSearch && matchesProvider;
    });
  }, [connections, searchText, providerFilter]);

  // Table columns for list view
  const columns = [
    {
      title: 'Name',
      dataIndex: 'name',
      key: 'name',
      render: (text, record) => (
        <Space>
          <DatabaseOutlined />
          {text}
          {record.id === activeConnection?.id && (
            <Tag color="blue">Active</Tag>
          )}
          {record.isDefault && <Tag color="green">Default</Tag>}
        </Space>
      ),
    },
    {
      title: 'Provider',
      dataIndex: 'provider',
      key: 'provider',
      render: (provider) => (
        <Tag color={getProviderColor(provider)}>{provider}</Tag>
      ),
    },
    {
      title: 'Host',
      dataIndex: 'host',
      key: 'host',
      render: (host, record) => `${host}:${record.port}/${record.database}`,
    },
    {
      title: 'Schema',
      dataIndex: 'schemaSync',
      key: 'schemaSync',
      render: (schemaSync) =>
        schemaSync?.isSynced ? (
          <Tag color="success">
            {schemaSync.tableCount} tables
          </Tag>
        ) : (
          <Tag color="warning">Not synced</Tag>
        ),
    },
    {
      title: 'Actions',
      key: 'actions',
      render: (_, record) => (
        <Space>
          <Button
            type="primary"
            size="small"
            onClick={() => onConnect?.(record)}
          >
            Connect
          </Button>
          <Button size="small" onClick={() => onEdit?.(record)}>
            Edit
          </Button>
          <Button
            size="small"
            onClick={() => onTest?.(record.id)}
            loading={isTesting}
          >
            Test
          </Button>
          <Button
            size="small"
            onClick={() => onSync?.(record.id)}
            loading={isSyncing}
          >
            Sync
          </Button>
          <Button
            size="small"
            danger
            onClick={() => onDelete?.(record.id)}
          >
            Delete
          </Button>
        </Space>
      ),
    },
  ];

  // Get provider color
  const getProviderColor = (provider) => {
    const colors = {
      SqlServer: '#CC2927',
      PostgreSql: '#336791',
      MySql: '#4479A1',
      Sqlite: '#003B57',
    };
    return colors[provider] || '#1890ff';
  };

  // Empty state
  const renderEmpty = () => (
    <Empty
      image={Empty.PRESENTED_IMAGE_SIMPLE}
      description={
        <Space direction="vertical" align="center">
          <Title level={4}>No Connections Yet</Title>
          <Text type="secondary">
            Create your first database connection to get started.
          </Text>
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={onAddNew}
            size="large"
          >
            Create Connection
          </Button>
        </Space>
      }
    />
  );

  // Grid view
  const renderGridView = () => (
    <Row gutter={[16, 16]}>
      {filteredConnections.map((connection) => (
        <Col xs={24} sm={12} lg={8} xl={6} key={connection.id}>
          <ConnectionCard
            connection={connection}
            isActive={connection.id === activeConnection?.id}
            onConnect={onConnect}
            onEdit={onEdit}
            onDelete={onDelete}
            onTest={onTest}
            onSync={onSync}
            isTesting={isTesting}
            isSyncing={isSyncing}
          />
        </Col>
      ))}
    </Row>
  );

  // List view (table)
  const renderListView = () => (
    <Table
      columns={columns}
      dataSource={filteredConnections}
      rowKey="id"
      loading={isLoading}
      pagination={{ pageSize: 10 }}
    />
  );

  return (
    <div>
      {/* Header */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          marginBottom: 16,
        }}
      >
        <Space>
          <Input
            placeholder="Search connections..."
            prefix={<SearchOutlined />}
            value={searchText}
            onChange={(e) => setSearchText(e.target.value)}
            style={{ width: 250 }}
            allowClear
          />
          <Select
            value={providerFilter}
            onChange={setProviderFilter}
            style={{ width: 150 }}
          >
            {PROVIDER_FILTERS.map((filter) => (
              <Option key={filter.value} value={filter.value}>
                {filter.label}
              </Option>
            ))}
          </Select>
        </Space>

        <Space>
          <Segmented
            value={viewMode}
            onChange={setViewMode}
            options={[
              {
                value: 'grid',
                icon: <AppstoreOutlined />,
              },
              {
                value: 'list',
                icon: <UnorderedListOutlined />,
              },
            ]}
          />
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={onAddNew}
          >
            New Connection
          </Button>
        </Space>
      </div>

      {/* Content */}
      {isLoading ? (
        <div style={{ textAlign: 'center', padding: 48 }}>
          <Spin size="large" />
        </div>
      ) : filteredConnections.length === 0 ? (
        renderEmpty()
      ) : viewMode === 'grid' ? (
        renderGridView()
      ) : (
        renderListView()
      )}
    </div>
  );
};

export default ConnectionList;
