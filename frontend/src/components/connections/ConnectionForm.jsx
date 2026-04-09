/**
 * ConnectionForm - Form component for creating/editing database connections
 * Uses Ant Design Form with validation
 */
import { useEffect } from 'react';
import {
  Form,
  Input,
  Select,
  InputNumber,
  Switch,
  Divider,
  Space,
  Button,
  Typography,
} from 'antd';
import {
  DatabaseOutlined,
  LockOutlined,
  UserOutlined,
  GlobalOutlined,
  InfoCircleOutlined,
} from '@ant-design/icons';

const { Title, Text } = Typography;
const { Option } = Select;

/**
 * Database provider options with default ports
 */
const PROVIDER_OPTIONS = [
  { value: 'SqlServer', label: 'SQL Server', defaultPort: 1433 },
  { value: 'PostgreSql', label: 'PostgreSQL', defaultPort: 5432 },
  { value: 'MySql', label: 'MySQL', defaultPort: 3306 },
  { value: 'Sqlite', label: 'SQLite', defaultPort: 0 },
];

/**
 * Get default port for a provider
 * @param {string} provider - Database provider
 * @returns {number} Default port
 */
const getDefaultPort = (provider) => {
  const option = PROVIDER_OPTIONS.find((p) => p.value === provider);
  return option?.defaultPort || 1433;
};

/**
 * ConnectionForm Component
 * @param {Object} props
 * @param {Object} props.initialValues - Initial form values (for editing)
 * @param {boolean} props.isEditing - Whether in edit mode
 * @param {Function} props.onSubmit - Form submit handler
 * @param {Function} props.onTest - Test connection handler
 * @param {boolean} props.isTesting - Whether test is in progress
 * @param {boolean} props.loading - Whether form is submitting
 */
const ConnectionForm = ({
  initialValues,
  isEditing = false,
  onSubmit,
  onTest,
  isTesting = false,
  loading = false,
}) => {
  const [form] = Form.useForm();

  // Set initial values when provided
  useEffect(() => {
    if (initialValues) {
      form.setFieldsValue(initialValues);
    }
  }, [initialValues, form]);

  // Handle provider change to update default port
  const handleProviderChange = (provider) => {
    const defaultPort = getDefaultPort(provider);
    form.setFieldsValue({ port: defaultPort });
  };

  // Handle form submission
  const handleSubmit = async (values) => {
    if (onSubmit) {
      await onSubmit(values);
    }
  };

  // Handle test connection
  const handleTest = async () => {
    try {
      const values = await form.validateFields();
      if (onTest) {
        await onTest(values);
      }
    } catch {
      // Validation failed - fields will show their error state via Ant Design
    }
  };

  return (
    <Form
      form={form}
      layout="vertical"
      onFinish={handleSubmit}
      initialValues={{
        provider: 'SqlServer',
        port: 1433,
        isDefault: false,
        ...initialValues,
      }}
      disabled={loading}
    >
      {/* Connection Name */}
      <Form.Item
        name="name"
        label="Connection Name"
        rules={[
          { required: true, message: 'Please enter a connection name' },
          { max: 100, message: 'Name cannot exceed 100 characters' },
        ]}
      >
        <Input
          prefix={<DatabaseOutlined />}
          placeholder="My Database Connection"
          size="large"
        />
      </Form.Item>

      {/* Database Provider */}
      <Form.Item
        name="provider"
        label="Database Provider"
        rules={[{ required: true, message: 'Please select a database provider' }]}
      >
        <Select
          placeholder="Select database provider"
          onChange={handleProviderChange}
          size="large"
        >
          {PROVIDER_OPTIONS.map((provider) => (
            <Option key={provider.value} value={provider.value}>
              {provider.label}
            </Option>
          ))}
        </Select>
      </Form.Item>

      <Divider orientation="left">Connection Details</Divider>

      {/* Host and Port Row */}
      <Space size="large" style={{ display: 'flex', width: '100%' }}>
        <Form.Item
          name="host"
          label="Host"
          rules={[{ required: true, message: 'Please enter the host' }]}
          style={{ flex: 1 }}
        >
          <Input
            prefix={<GlobalOutlined />}
            placeholder="localhost or IP address"
          />
        </Form.Item>

        <Form.Item
          name="port"
          label="Port"
          rules={[{ required: true, message: 'Please enter the port' }]}
          style={{ width: 120 }}
        >
          <InputNumber
            min={1}
            max={65535}
            placeholder="1433"
            style={{ width: '100%' }}
          />
        </Form.Item>
      </Space>

      {/* Database Name */}
      <Form.Item
        name="database"
        label="Database Name"
        rules={[
          { required: true, message: 'Please enter the database name' },
          { max: 100, message: 'Database name cannot exceed 100 characters' },
        ]}
      >
        <Input prefix={<DatabaseOutlined />} placeholder="my_database" />
      </Form.Item>

      {/* Username and Password Row */}
      <Space size="large" style={{ display: 'flex', width: '100%' }}>
        <Form.Item
          name="username"
          label="Username"
          rules={[{ required: true, message: 'Please enter the username' }]}
          style={{ flex: 1 }}
        >
          <Input prefix={<UserOutlined />} placeholder="sa" />
        </Form.Item>

        <Form.Item
          name="password"
          label="Password"
          rules={[
            {
              required: !isEditing,
              message: 'Please enter the password',
            },
          ]}
          style={{ flex: 1 }}
        >
          <Input.Password
            prefix={<LockOutlined />}
            placeholder={isEditing ? 'Leave empty to keep current' : 'Enter password'}
          />
        </Form.Item>
      </Space>

      <Divider orientation="left">Additional Options</Divider>

      {/* Description */}
      <Form.Item
        name="description"
        label="Description (Optional)"
        rules={[{ max: 500, message: 'Description cannot exceed 500 characters' }]}
      >
        <Input.TextArea
          placeholder="Add a description for this connection..."
          rows={2}
        />
      </Form.Item>

      {/* Default Connection Switch */}
      <Form.Item name="isDefault" valuePropName="checked">
        <Space>
          <Switch checked={form.getFieldValue('isDefault')} />
          <Text>
            Set as default connection
            <Text type="secondary" style={{ display: 'block', fontSize: 12 }}>
              Default connection will be auto-selected when available
            </Text>
          </Text>
        </Space>
      </Form.Item>

      <Divider orientation="left">
        <Space>
          <InfoCircleOutlined />
          AI Context (Optional)
        </Space>
      </Divider>

      <Text type="secondary" style={{ display: 'block', marginBottom: 16 }}>
        Provide context to help AI better understand your database schema and naming conventions.
        This improves accuracy for schema analysis, column interpretation, and query suggestions.
      </Text>

      {/* System Domain */}
      <Form.Item
        name="systemDomain"
        label="System Domain"
        tooltip="What type of system is this database for? (e.g., E-commerce, ERP, CRM)"
      >
        <Select
          placeholder="Select system domain"
          size="large"
          allowClear
        >
          <Option value="E-commerce">E-commerce / Bán lẻ</Option>
          <Option value="ERP">ERP / Quản lý doanh nghiệp</Option>
          <Option value="CRM">CRM / Quản lý khách hàng</Option>
          <Option value="Healthcare">Healthcare / Y tế</Option>
          <Option value="Education">Education / Giáo dục</Option>
          <Option value="Finance">Finance / Tài chính</Option>
          <Option value="Manufacturing">Manufacturing / Sản xuất</Option>
          <Option value="Logistics">Logistics / Vận chuyển</Option>
          <Option value="HR">HR / Nhân sự</Option>
          <Option value="Other">Other / Khác</Option>
        </Select>
      </Form.Item>

      {/* Naming Convention Notes */}
      <Form.Item
        name="namingConventionNotes"
        label="Naming Convention Notes"
        tooltip="Explain your naming patterns (e.g., 'Ma = Mã, Ten = Tên, DM = Danh mục')"
        rules={[{ max: 500, message: 'Notes cannot exceed 500 characters' }]}
      >
        <Input.TextArea
          placeholder="Example: Tên bảng dùng PascalCase. Tên cột viết tắt tiếng Việt: Ma = Mã, Ten = Tên, DM = Danh mục. Foreign key pattern: Ma{Table}"
          rows={3}
        />
      </Form.Item>

      {/* Business Context */}
      <Form.Item
        name="businessContext"
        label="Business Context"
        tooltip="Describe what this system does and any important business rules"
        rules={[{ max: 1000, message: 'Context cannot exceed 1000 characters' }]}
      >
        <Input.TextArea
          placeholder="Example: Hệ thống ERP cho công ty sản xuất thép. Quản lý đơn hàng, kho, sản xuất, và kế toán. Database được migrate từ hệ thống cũ nên có một số bảng legacy."
          rows={4}
        />
      </Form.Item>

      {/* Form Actions */}
      <Form.Item style={{ marginBottom: 0, marginTop: 24 }}>
        <Space style={{ width: '100%', justifyContent: 'flex-end' }}>
          <Button
            onClick={handleTest}
            loading={isTesting}
            disabled={loading}
          >
            Test Connection
          </Button>
          <Button onClick={() => form.resetFields()}>Reset</Button>
          <Button
            type="primary"
            htmlType="submit"
            loading={loading}
            disabled={isTesting}
          >
            {isEditing ? 'Update Connection' : 'Create Connection'}
          </Button>
        </Space>
      </Form.Item>
    </Form>
  );
};

export default ConnectionForm;
