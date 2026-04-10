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

const ConnectionsPage = () => {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingConnection, setEditingConnection] = useState(null);
  const [connectingId, setConnectingId] = useState(null);
  const [connectDialog, setConnectDialog] = useState({
    open: false,
    record: null,
    status: 'idle',
    message: '',
    result: null,
    indexStatus: null,
    startedAt: null,
    elapsedSeconds: 0,
    polling: false,
  });
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

  useEffect(() => {
    if (!connectDialog.open || !connectDialog.startedAt) {
      return undefined;
    }

    const timer = setInterval(() => {
      setConnectDialog((prev) => {
        if (!prev.open || !prev.startedAt) {
          return prev;
        }

        return {
          ...prev,
          elapsedSeconds: Math.max(0, Math.floor((Date.now() - prev.startedAt) / 1000)),
        };
      });
    }, 1000);

    return () => clearInterval(timer);
  }, [connectDialog.open, connectDialog.startedAt]);

  useEffect(() => {
    if (!connectDialog.polling || !connectDialog.record?.id) {
      return undefined;
    }

    let cancelled = false;
    const connectionId = connectDialog.record.id;
    const connectionName = connectDialog.record.name;

    const pollStatus = async () => {
      try {
        const response = await axiosInstance.get(`/api/connections/${connectionId}/indexing-status`);
        if (cancelled) {
          return;
        }

        const status = response.data;

        setConnectDialog((prev) => {
          if (prev.record?.id !== connectionId) {
            return prev;
          }

          return {
            ...prev,
            indexStatus: status,
            message: status.message || prev.message,
            polling: ['queued', 'indexing'].includes(status.status),
          };
        });

        if (status.status === 'completed') {
          await fetchConnections(true);
          message.success(`Semantic index for ${connectionName} is ready.`);
        } else if (status.status === 'failed') {
          await fetchConnections(true);
          message.warning(`Semantic index for ${connectionName} failed. Chat will continue with cached schema.`);
        }
      } catch {
        if (!cancelled) {
          setConnectDialog((prev) => (
            prev.record?.id === connectionId
              ? { ...prev, polling: false }
              : prev
          ));
        }
      }
    };

    pollStatus();
    const interval = setInterval(pollStatus, 3000);

    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [connectDialog.polling, connectDialog.record?.id, connectDialog.record?.name, fetchConnections]);

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
    setConnectDialog({
      open: true,
      record,
      status: 'processing',
      message: 'Verifying database connection and loading schema...',
      result: null,
      indexStatus: null,
      startedAt: Date.now(),
      elapsedSeconds: 0,
      polling: false,
    });

    try {
      const response = await axiosInstance.post(`/api/connections/${record.id}/test-enhanced`);
      const result = response.data;
      const schemaIndexing = result.schemaIndexing || {};
      const isBackgroundIndexing = ['queued', 'indexing'].includes(schemaIndexing.status);
      const canUseChat = !!result.readyForChat;

      if (!result.success) {
        setConnectDialog((prev) => ({
          ...prev,
          status: 'failed',
          message: result.databaseConnection?.errorMessage || 'Database connection failed',
          result,
        }));
        message.error(`Connection failed: ${result.databaseConnection?.errorMessage || 'Unknown error'}`);
        return;
      }

      if (canUseChat) {
        setActiveConnection({
          ...record,
          isConnected: true,
        });
      }

      setConnectDialog((prev) => ({
        ...prev,
        status: canUseChat ? 'completed' : 'failed',
        message: schemaIndexing.statusMessage || (canUseChat
          ? 'Connection is ready for chat.'
          : 'Connection succeeded but schema is not ready yet.'),
        result,
        indexStatus: isBackgroundIndexing ? schemaIndexing : null,
        polling: isBackgroundIndexing,
      }));

      await fetchConnections(true);

      if (canUseChat && isBackgroundIndexing) {
        message.success(`Connected to ${record.name}. You can start chatting while semantic indexing continues in the background.`);
      } else if (canUseChat) {
        message.success(`Connected to ${record.name} - Ready for chat!`);
      } else {
        message.warning('Database connected, but chat is waiting for schema to finish loading.');
      }
    } catch (error) {
      console.error('Connection test error:', error);
      setConnectDialog((prev) => ({
        ...prev,
        status: 'failed',
        message: error.response?.data?.message || error.message || 'Connection failed',
      }));
      message.error(error.response?.data?.message || error.response?.data?.errorMessage || 'Failed to connect to database');
    } finally {
      setConnectingId(null);
    }
  };

  const handleCloseConnectDialog = () => {
    setConnectDialog((prev) => ({
      ...prev,
      open: false,
      polling: false,
    }));
  };

  const handleOpenChat = () => {
    setConnectDialog((prev) => ({
      ...prev,
      open: false,
      polling: false,
    }));
    navigate('/chat');
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
          </Space>
        );
      },
    },
  ];

  const currentIndexStatus = connectDialog.indexStatus || connectDialog.result?.schemaIndexing || {};
  const canOpenChat = !!connectDialog.result?.readyForChat;
  const isSemanticIndexingActive = ['queued', 'indexing'].includes(currentIndexStatus.status);
  const modalStep = !connectDialog.result
    ? 0
    : isSemanticIndexingActive
      ? 2
      : canOpenChat
        ? 3
        : 1;
  const modalStepStatus = connectDialog.status === 'failed' && !canOpenChat
    ? 'error'
    : isSemanticIndexingActive
      ? 'process'
      : canOpenChat
        ? 'finish'
        : 'error';
  const modalProgress = !connectDialog.result
    ? 20
    : currentIndexStatus.progressPercent || (canOpenChat ? 100 : 60);
  const modalAlertType = connectDialog.status === 'failed' && !canOpenChat
    ? 'error'
    : currentIndexStatus.status === 'failed'
      ? 'warning'
      : isSemanticIndexingActive
        ? 'info'
        : 'success';

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
        title={connectDialog.record ? `Connecting to ${connectDialog.record.name}` : 'Connecting'}
        open={connectDialog.open}
        onCancel={handleCloseConnectDialog}
        maskClosable={!connectingId}
        closable={!connectingId}
        footer={[
          <Button key="close" onClick={handleCloseConnectDialog} disabled={!!connectingId}>
            {canOpenChat ? 'Stay Here' : 'Close'}
          </Button>,
          canOpenChat && (
            <Button key="chat" type="primary" onClick={handleOpenChat}>
              Go to Chat
            </Button>
          ),
        ].filter(Boolean)}
      >
        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
          <Steps
            size="small"
            current={modalStep}
            status={modalStepStatus}
            items={[
              { title: 'Database' },
              { title: 'Schema Cache' },
              { title: 'Semantic Index' },
              { title: 'Chat Ready' },
            ]}
          />

          <Alert
            type={modalAlertType}
            showIcon
            message={connectDialog.message || 'Preparing your connection...'}
            description={
              isSemanticIndexingActive
                ? 'You can open Chat now. Qdrant indexing continues in the background, so the first semantic lookups may be slower.'
                : canOpenChat
                  ? 'The schema is already cached for the agent, so you can start querying right away.'
                  : 'The connection is still being prepared. Please review the details below.'
            }
          />

          <div>
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 6 }}>
              <span style={{ fontSize: 12, color: '#666' }}>Elapsed time</span>
              <span style={{ fontSize: 12, color: '#666' }}>{connectDialog.elapsedSeconds}s</span>
            </div>
            <Progress
              percent={modalProgress}
              status={!connectDialog.result
                ? 'active'
                : currentIndexStatus.status === 'failed' && !canOpenChat
                  ? 'exception'
                  : isSemanticIndexingActive
                    ? 'active'
                    : 'success'}
              showInfo
            />
          </div>

          <div style={{ display: 'grid', gap: 8 }}>
            <div style={{ fontSize: 13 }}>
              <strong>Database:</strong>{' '}
              {connectDialog.result?.databaseConnection?.success
                ? `Connected in ${connectDialog.result.databaseConnection.responseTime}`
                : 'Pending'}
            </div>
            <div style={{ fontSize: 13 }}>
              <strong>Version:</strong>{' '}
              {connectDialog.result?.databaseConnection?.databaseVersion || 'Pending'}
            </div>
            <div style={{ fontSize: 13 }}>
              <strong>Schema:</strong>{' '}
              {currentIndexStatus.tableCount || 0} tables, {currentIndexStatus.columnCount || 0} columns, {currentIndexStatus.relationshipCount || 0} relationships
            </div>
            <div style={{ fontSize: 13 }}>
              <strong>Qdrant coverage:</strong>{' '}
              {(currentIndexStatus.indexedPointCount ?? currentIndexStatus.schemasIndexed ?? 0)}/{currentIndexStatus.expectedPointCount || 0} points
              {currentIndexStatus.fingerprintMatched ? ' (current fingerprint match)' : ''}
            </div>
            {currentIndexStatus.errorMessage && (
              <div style={{ fontSize: 13, color: '#ff4d4f' }}>
                <strong>Index issue:</strong> {currentIndexStatus.errorMessage}
              </div>
            )}
          </div>
        </Space>
      </Modal>

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
