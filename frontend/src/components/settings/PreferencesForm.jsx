import { useState, useEffect } from 'react';
import { 
  Card, 
  Form, 
  Input, 
  Select, 
  Switch, 
  Button, 
  InputNumber, 
  Slider, 
  Space, 
  Divider, 
  Typography, 
  message,
  Popconfirm,
  Row, 
  Col,
  Tag,
  Alert,
} from 'antd';
import {
  DesktopOutlined,
  BellOutlined,
  SaveOutlined,
  DeleteOutlined,
  DownloadOutlined,
  SecurityScanOutlined,
  GlobalOutlined,
  AppstoreOutlined,
  KeyOutlined,
  CrownOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons';

const { Text, Title } = Typography;

const STORAGE_KEY = 'user_preferences';

const defaultPreferences = {
  defaultModel: 'gpt-4',
  temperature: 0.7,
  maxTokens: 2048,
  quotaWarningEnabled: true,
  quotaWarningThreshold: 80,
  emailAlertsEnabled: false,
  autoStopEnabled: false,
  autoSaveHistory: true,
  semanticSearchEnabled: true,
  autoDeleteDays: 30,
  defaultView: 'chat',
  language: 'en',
  tableRows: 25,
  customApiKey: '',
  customApiEndpoint: '',
};

const PreferencesForm = () => {
  const [form] = Form.useForm();
  const [loading, setLoading] = useState(false);
  const [preferences, setPreferences] = useState(defaultPreferences);

  useEffect(() => {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved) {
      try {
        const parsed = JSON.parse(saved);
        setPreferences({ ...defaultPreferences, ...parsed });
        form.setFieldsValue(parsed);
      } catch (e) {
        console.error('Failed to parse preferences:', e);
      }
    }
  }, [form]);

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      setLoading(true);
      
      localStorage.setItem(STORAGE_KEY, JSON.stringify(values));
      setPreferences(values);
      
      message.success('Preferences saved!');
    } catch (error) {
      console.error('Save failed:', error);
      message.error('Failed to save preferences');
    } finally {
      setLoading(false);
    }
  };

  const handleReset = () => {
    form.setFieldsValue(defaultPreferences);
    localStorage.removeItem(STORAGE_KEY);
    setPreferences(defaultPreferences);
    message.info('Preferences reset to defaults');
  };

  const handleExportData = () => {
    const data = {
      preferences: preferences,
      exportedAt: new Date().toISOString(),
    };
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `texttosql_preferences_${Date.now()}.json`;
    a.click();
    URL.revokeObjectURL(url);
    message.success('Preferences exported!');
  };

  return (
    <div>
      {/* Quota & Plan */}
      <Card 
        title={
          <Space>
            <CrownOutlined />
            Quota & Plan
          </Space>
        }
        style={{ marginBottom: 16 }}
      >
        <Row gutter={[16, 16]}>
          <Col xs={24} md={12}>
            <div style={{ 
              padding: 16, 
              background: '#f0f5ff', 
              borderRadius: 8,
              border: '1px solid #d6e4ff'
            }}>
              <Space direction="vertical">
                <Space>
                  <ThunderboltOutlined style={{ color: '#1890ff', fontSize: 20 }} />
                  <Title level={5} style={{ margin: 0 }}>Current Plan</Title>
                </Space>
                <Tag color="blue" style={{ fontSize: 14, padding: '4px 12px' }}>Free Tier</Tag>
                <Text type="secondary">10,000 tokens/day</Text>
              </Space>
            </div>
          </Col>
          <Col xs={24} md={12}>
            <div style={{ 
              padding: 16, 
              background: '#f6ffed', 
              borderRadius: 8,
              border: '1px solid #b7eb8f'
            }}>
              <Space direction="vertical">
                <Space>
                  <CrownOutlined style={{ color: '#52c41a', fontSize: 20 }} />
                  <Title level={5} style={{ margin: 0 }}>Upgrade to Pro</Title>
                </Space>
                <Text>Unlimited queries, faster responses, priority support</Text>
                <Button type="primary" icon={<CrownOutlined />}>
                  Upgrade Now
                </Button>
              </Space>
            </div>
          </Col>
        </Row>
      </Card>

      {/* API & Performance */}
      <Card 
        title={
          <Space>
            <DesktopOutlined />
            API & Performance
          </Space>
        }
        style={{ marginBottom: 16 }}
      >
        <Form form={form} layout="vertical">
          <Row gutter={16}>
            <Col xs={24} md={12}>
              <Form.Item name="defaultModel" label="Default Model">
                <Select>
                  <Select.Option value="gpt-4">GPT-4 (Recommended)</Select.Option>
                  <Select.Option value="gpt-4-turbo">GPT-4 Turbo</Select.Option>
                  <Select.Option value="gpt-3.5-turbo">GPT-3.5 Turbo (Faster)</Select.Option>
                </Select>
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="temperature" label="Temperature">
                <div>
                  <Slider min={0} max={2} step={0.1} marks={{ 0: '0', 1: '1', 2: '2' }} />
                  <Text type="secondary" style={{ fontSize: 12 }}>
                    Lower = more focused, Higher = more creative
                  </Text>
                </div>
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="maxTokens" label="Max Tokens">
            <InputNumber min={256} max={8192} step={256} style={{ width: '100%' }} />
          </Form.Item>
        </Form>
      </Card>

      {/* Notifications */}
      <Card 
        title={
          <Space>
            <BellOutlined />
            Notifications & Alerts
          </Space>
        }
        style={{ marginBottom: 16 }}
      >
        <Form form={form} layout="vertical">
          <Form.Item name="quotaWarningEnabled" label="Quota Warning" valuePropName="checked">
            <Switch />
          </Form.Item>
          {preferences.quotaWarningEnabled && (
            <Form.Item name="quotaWarningThreshold" label="Warning at (%)">
              <InputNumber min={50} max={100} step={10} />
            </Form.Item>
          )}
          <Divider />
          <Form.Item name="emailAlertsEnabled" label="Email Alerts" valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="autoStopEnabled" label="Auto-stop when quota exceeded" valuePropName="checked">
            <Switch />
          </Form.Item>
        </Form>
      </Card>

      {/* Query Settings */}
      <Card 
        title={
          <Space>
            <SaveOutlined />
            Query Settings
          </Space>
        }
        style={{ marginBottom: 16 }}
      >
        <Form form={form} layout="vertical">
          <Form.Item name="autoSaveHistory" label="Auto-save Query History" valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="semanticSearchEnabled" label="Semantic Search in History" valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="autoDeleteDays" label="Auto-delete history after (days)">
            <Select>
              <Select.Option value={7}>7 days</Select.Option>
              <Select.Option value={14}>14 days</Select.Option>
              <Select.Option value={30}>30 days</Select.Option>
              <Select.Option value={90}>90 days</Select.Option>
              <Select.Option value={365}>Never</Select.Option>
            </Select>
          </Form.Item>
        </Form>
      </Card>

      {/* Data Management */}
      <Card 
        title={
          <Space>
            <DownloadOutlined />
            Data Management
          </Space>
        }
        style={{ marginBottom: 16 }}
      >
        <Space direction="vertical" style={{ width: '100%' }} size="middle">
          <Space>
            <Button icon={<DownloadOutlined />} onClick={handleExportData}>
              Export Preferences
            </Button>
          </Space>
          <Divider />
          <Alert
            type="warning"
            message="Clear All Data"
            description="This will permanently delete all your conversations and query history."
          />
          <Popconfirm
            title="Are you sure?"
            description="This action cannot be undone."
            okText="Delete"
            okButtonProps={{ danger: true }}
          >
            <Button danger icon={<DeleteOutlined />}>
              Clear All History
            </Button>
          </Popconfirm>
        </Space>
      </Card>

      {/* UI Preferences */}
      <Card 
        title={
          <Space>
            <AppstoreOutlined />
            UI Preferences
          </Space>
        }
        style={{ marginBottom: 16 }}
      >
        <Form form={form} layout="vertical">
          <Row gutter={16}>
            <Col xs={24} md={12}>
              <Form.Item name="defaultView" label="Default View">
                <Select>
                  <Select.Option value="chat">Chat</Select.Option>
                  <Select.Option value="query-lab">Query Lab</Select.Option>
                  <Select.Option value="explorer">DB Explorer</Select.Option>
                </Select>
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="tableRows" label="Table Rows Per Page">
                <Select>
                  <Select.Option value={10}>10</Select.Option>
                  <Select.Option value={25}>25</Select.Option>
                  <Select.Option value={50}>50</Select.Option>
                  <Select.Option value={100}>100</Select.Option>
                </Select>
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="language" label="Language">
            <Select>
              <Select.Option value="en">English</Select.Option>
              <Select.Option value="vi">Vietnamese</Select.Option>
            </Select>
          </Form.Item>
        </Form>
      </Card>

      {/* Developer Options */}
      <Card 
        title={
          <Space>
            <KeyOutlined />
            Developer Options
          </Space>
        }
        style={{ marginBottom: 16 }}
      >
        <Alert
          type="info"
          message="Advanced Settings"
          description="Use your own OpenAI API key to avoid quota limits."
          style={{ marginBottom: 16 }}
        />
        <Form form={form} layout="vertical">
          <Form.Item name="customApiKey" label="Custom OpenAI API Key">
            <Input.Password placeholder="sk-..." />
          </Form.Item>
          <Form.Item name="customApiEndpoint" label="Custom API Endpoint (optional)">
            <Input placeholder="https://api.openai.com/v1" />
          </Form.Item>
        </Form>
      </Card>

      {/* Actions */}
      <Space>
        <Button type="primary" icon={<SaveOutlined />} onClick={handleSave} loading={loading}>
          Save Preferences
        </Button>
        <Button icon={<SecurityScanOutlined />} onClick={handleReset}>
          Reset to Defaults
        </Button>
      </Space>
    </div>
  );
};

export default PreferencesForm;