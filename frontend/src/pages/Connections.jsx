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
} from 'antd';
import { 
  PlusOutlined, 
  EditOutlined, 
  DeleteOutlined, 
  DatabaseOutlined,
  CheckCircleOutlined,
} from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import axiosInstance from '../api/axios';
import useConnectionStore from '../store/connectionStore';

const { Title } = Typography;
const { Option } = Select;

const ConnectionsPage = () => {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingConnection, setEditingConnection] = useState(null);
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
    try {
      // Test the connection first using the API
      await axiosInstance.post(`/api/connections/${record.id}/test`, record);
      
      // If test succeeds, set as active and navigate to chat
      setActiveConnection(record);
      message.success(`Connected to ${record.name}`);
      navigate('/chat');
    } catch (error) {
      message.error(error.response?.data?.message || error.response?.data?.errorMessage || 'Failed to connect to database');
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
      title: 'Actions',
      key: 'actions',
      render: (_, record) => (
        <Space>
          <Button 
            type="link" 
            icon={<CheckCircleOutlined />} 
            onClick={() => handleConnect(record)}
          >
            Connect
          </Button>
          <Button 
            type="link" 
            icon={<EditOutlined />} 
            onClick={() => handleEdit(record)}
          >
            Edit
          </Button>
          <Popconfirm
            title="Are you sure you want to delete this connection?"
            onConfirm={() => handleDelete(record.id)}
            okText="Yes"
            cancelText="No"
          >
            <Button type="link" danger icon={<DeleteOutlined />}>
              Delete
            </Button>
          </Popconfirm>
        </Space>
      ),
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
        loading={isLoading}
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
