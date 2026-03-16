import { useEffect, useState } from 'react';
import {
  Table,
  Button,
  Space,
  Modal,
  Form,
  Input,
  Select,
  message,
  Popconfirm,
  Typography,
  Tag,
  Progress,
  Steps,
  Alert,
} from 'antd';
import {
  PlusOutlined,
  EditOutlined,
  DeleteOutlined,
  DatabaseOutlined,
  CheckCircleOutlined,
  LoadingOutlined,
  ExclamationCircleOutlined,
} from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { TableSkeleton } from '../components/common';
import axiosInstance from '../api/axios';
import useConnectionStore from '../store/connectionStore';

const { Title } = Typography;
const { Option } = Select;
const { Step } = Steps;

const ConnectionsPage = () => {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingConnection, setEditingConnection] = useState(null);
  const [connectingId, setConnectingId] = useState(null);
  const [connectionProgress, setConnectionProgress] = useState({});
  const [form] = Form.useForm();
  const navigate = useNavigate();

  const {
    connections,
    isLoading,
    fetchConnections,
    createConnection,
    updateConnection,
    deleteConnection,
    setActiveConnection,
  } = useConnectionStore();

  useEffect(() => {
    fetchConnections();
  }, [fetchConnections]);

  const handleSubmit = async (values) => {
    try {
      if (editingConnection) {
        await updateConnection(editingConnection.id, values);
        message.success('Connection updated successfully');
      } else {
        await createConnection(values);
        message.success('Connection created successfully');
      }
      setIsModalOpen(false);
      form.resetFields();
      setEditingConnection(null);
    } catch (error) {
      message.error(error.response?.data?.message || 'Operation failed');
    }
  };

  const handleEdit = (record) => {
    setEditingConnection(record);
    form.setFieldsValue(record);
    setIsModalOpen(true);
  };

  const handleDelete = async (id) => {
    try {
      await deleteConnection(id);
      message.success('Connection deleted successfully');
    } catch {
      message.error('Failed to delete connection');
    }
  };

  const handleConnect = async (record) => {
    setConnectingId(record.id);
    setConnectionProgress({
      step: 0,
      status: 'process',
      message: 'Testing database connection...'
    });

    try {
      // Use enhanced test endpoint with progress tracking
      const response = await axiosInstance.post(`/api/connections/${record.id}/test-enhanced`);
      const result = response.data;

      if (!result.success) {
        setConnectionProgress({
          step: 0,
          status: 'error',
          message: result.databaseConnection?.errorMessage || 'Database connection failed'
        });
        message.error(`Connection failed: ${result.databaseConnection?.errorMessage || 'Unknown error'}`);
        return;
      }

      // Step 1: Database connection successful
      setConnectionProgress({
        step: 1,
        status: 'process',
        message: `Database connected (${result.databaseConnection.responseTime})`
      });

      // Step 2: Check schema indexing
      if (result.schemaIndexing?.autoIndexed) {
        setConnectionProgress({
          step: 2,
          status: 'process',
          message: `Schema indexed automatically (${result.schemaIndexing.indexingTime})`
        });
      } else if (result.schemaIndexing?.collectionExists) {
        setConnectionProgress({
          step: 2,
          status: 'finish',
          message: `Schema already indexed (${result.schemaIndexing.schemasIndexed} schemas)`
        });
      }

      // Final step: Ready for chat
      if (result.readyForChat) {
        setConnectionProgress({
          step: 3,
          status: 'finish',
          message: 'Ready for chat!'
        });

        // Set as active and navigate to chat
        setActiveConnection(record);
        message.success(`Connected to ${record.name} - Ready for chat!`);

        // Small delay to show success state
        setTimeout(() => {
          navigate('/chat');
        }, 1000);
      } else {
        setConnectionProgress({
          step: 2,
          status: 'error',
          message: result.schemaIndexing?.errorMessage || 'Schema indexing failed'
        });
        message.warning(`Connected to database but schema indexing failed. You may experience limited functionality.`);
      }

    } catch (error) {
      console.error('Connection test error:', error);
      setConnectionProgress({
        step: 0,
        status: 'error',
        message: error.response?.data?.message || error.message || 'Connection failed'
      });
      message.error(error.response?.data?.message || error.response?.data?.errorMessage || 'Failed to connect to database');
    } finally {
      // Clear progress after 3 seconds
      setTimeout(() => {
        setConnectingId(null);
        setConnectionProgress({});
      }, 3000);
    }
  };

  const columns = [
    {
      title: 'Name',
      dataIndex: 'name',
      key: 'name',
      render: (text) => <Space><DatabaseOutlined /> {text}</Space>,
    },
    {
      title: 'Provider',
      dataIndex: 'provider',
      key: 'provider',
      render: (provider) => (
        <Tag color="blue">{provider?.toUpperCase() || 'Unknown'}</Tag>
      ),
    },
    {
      title: 'Host',
      dataIndex: 'host',
      key: 'host',
    },
    {
      title: 'Database',
      dataIndex: 'database',
      key: 'database',
    },
    {
      title: 'Status',
      key: 'status',
      render: (_, record) => (
        <Space direction="vertical" size={2}>
          <Tag
            color={record.isConnected ? 'success' : 'default'}
            icon={record.isConnected ? <CheckCircleOutlined /> : <ExclamationCircleOutlined />}
          >
            {record.isConnected ? 'Connected' : 'Not Connected'}
          </Tag>
          {record.schemaSync && (
            <Tag
              color={record.schemaSync.isSynced ? 'blue' : 'warning'}
              style={{ fontSize: '11px' }}
            >
              {record.schemaSync.isSynced
                ? `${record.schemaSync.tableCount || 0} tables indexed`
                : 'Schema not indexed'
              }
            </Tag>
          )}
        </Space>
      ),
    },
    {
      title: 'Last Used',
      key: 'lastUsed',
      render: (_, record) => (
        <Space direction="vertical" size={2}>
          <span style={{ fontSize: '12px', color: '#666' }}>
            {record.lastUsedAt
              ? new Date(record.lastUsedAt).toLocaleDateString()
              : 'Never'
            }
          </span>
          {record.schemaSync?.lastSyncedAt && (
            <span style={{ fontSize: '11px', color: '#999' }}>
              Schema: {new Date(record.schemaSync.lastSyncedAt).toLocaleDateString()}
            </span>
          )}
        </Space>
      ),
    },
    {
      title: 'Actions',
      key: 'actions',
      render: (_, record) => {
        const isConnecting = connectingId === record.id;

        return (
          <Space direction="vertical" size="small" style={{ width: '100%' }}>
            <Space>
              <Button
                type="link"
                icon={isConnecting ? <LoadingOutlined /> : <CheckCircleOutlined />}
                onClick={() => handleConnect(record)}
                loading={isConnecting}
                disabled={isConnecting}
              >
                {isConnecting ? 'Connecting...' : 'Connect'}
              </Button>
              <Button
                type="link"
                icon={<EditOutlined />}
                onClick={() => handleEdit(record)}
                disabled={isConnecting}
              >
                Edit
              </Button>
              <Popconfirm
                title="Are you sure you want to delete this connection?"
                onConfirm={() => handleDelete(record.id)}
                okText="Yes"
                cancelText="No"
                disabled={isConnecting}
              >
                <Button type="link" danger icon={<DeleteOutlined />} disabled={isConnecting}>
                  Delete
                </Button>
              </Popconfirm>
            </Space>

            {/* Connection Progress Indicator */}
            {isConnecting && connectionProgress.message && (
              <div style={{ minWidth: 200 }}>
                <Steps
                  size="small"
                  current={connectionProgress.step}
                  status={connectionProgress.status}
                  items={[
                    {
                      title: 'Database',
                      icon: connectionProgress.step === 0 && connectionProgress.status === 'process' ? <LoadingOutlined /> : undefined,
                    },
                    {
                      title: 'Schema',
                      icon: connectionProgress.step === 1 && connectionProgress.status === 'process' ? <LoadingOutlined /> : undefined,
                    },
                    {
                      title: 'Ready',
                      icon: connectionProgress.step === 2 && connectionProgress.status === 'process' ? <LoadingOutlined /> : undefined,
                    },
                  ]}
                />
                <div style={{
                  fontSize: '12px',
                  color: connectionProgress.status === 'error' ? '#ff4d4f' : '#666',
                  marginTop: 4,
                  display: 'flex',
                  alignItems: 'center',
                  gap: 4
                }}>
                  {connectionProgress.status === 'error' && <ExclamationCircleOutlined />}
                  {connectionProgress.message}
                </div>
              </div>
            )}
          </Space>
        );
      },
    },
  ];

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 16 }}>
        <Title level={3} style={{ margin: 0 }}>Database Connections</Title>
        <Button
          type="primary"
          icon={<PlusOutlined />}
          onClick={() => {
            setEditingConnection(null);
            form.resetFields();
            setIsModalOpen(true);
          }}
        >
          New Connection
        </Button>
      </div>

      <Table
        columns={columns}
        dataSource={connections}
        loading={{
          spinning: isLoading,
          indicator: <TableSkeleton rows={5} columns={6} />
        }}
        rowKey="id"
        pagination={{ pageSize: 10 }}
      />

      <Modal
        title={editingConnection ? 'Edit Connection' : 'New Connection'}
        open={isModalOpen}
        onCancel={() => {
          setIsModalOpen(false);
          form.resetFields();
          setEditingConnection(null);
        }}
        footer={null}
      >
        <Form
          form={form}
          layout="vertical"
          onFinish={handleSubmit}
        >
          <Form.Item
            name="name"
            label="Connection Name"
            rules={[{ required: true, message: 'Please enter a name' }]}
          >
            <Input placeholder="My Database" />
          </Form.Item>

          <Form.Item
            name="provider"
            label="Database Provider"
            rules={[{ required: true, message: 'Please select a provider' }]}
          >
            <Select placeholder="Select provider">
              <Option value="SqlServer">SQL Server</Option>
              <Option value="PostgreSql">PostgreSQL</Option>
              <Option value="MySql">MySQL</Option>
              <Option value="Sqlite">SQLite</Option>
            </Select>
          </Form.Item>

          <Form.Item
            name="host"
            label="Host"
            rules={[{ required: true, message: 'Please enter the host' }]}
          >
            <Input placeholder="localhost" />
          </Form.Item>

          <Form.Item
            name="port"
            label="Port"
            rules={[{ required: true, message: 'Please enter the port' }]}
          >
            <Input type="number" placeholder="1433" />
          </Form.Item>

          <Form.Item
            name="database"
            label="Database Name"
            rules={[{ required: true, message: 'Please enter the database name' }]}
          >
            <Input placeholder="my_database" />
          </Form.Item>

          <Form.Item
            name="username"
            label="Username"
            rules={[{ required: true, message: 'Please enter the username' }]}
          >
            <Input placeholder="sa" />
          </Form.Item>

          <Form.Item
            name="password"
            label="Password"
            rules={[{ required: true, message: 'Please enter the password' }]}
          >
            <Input.Password placeholder="••••••••" />
          </Form.Item>

          <Form.Item>
            <Space style={{ width: '100%', justifyContent: 'flex-end' }}>
              <Button onClick={() => setIsModalOpen(false)}>Cancel</Button>
              <Button type="primary" htmlType="submit">
                {editingConnection ? 'Update' : 'Create'}
              </Button>
            </Space>
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default ConnectionsPage;