import { useState } from 'react';
import { Typography, Card, Form, Input, Button, message, Divider, Tabs, Row, Col } from 'antd';
import {
  UserOutlined,
  AppstoreOutlined,
  BarChartOutlined,
  SettingOutlined,
} from '@ant-design/icons';
import useAuthStore from '../store/authStore';
import { APP_NAME } from '../constants';
import {
  QuotaProgress,
  UsageChart,
  UsageByConversation,
  UsageByModel,
  QuotaProgressSkeleton,
  UsageChartSkeleton,
  UsageByModelSkeleton,
  UsageByConversationSkeleton,
} from '../components/dashboard';

const { Title, Text } = Typography;

const SettingsPage = () => {
  const { user } = useAuthStore();
  const [activeTab, setActiveTab] = useState('dashboard');

  const onFinish = () => {
    message.success('Settings saved successfully!');
  };

  const handleUpgradeClick = () => {
    message.info('Upgrade feature coming soon!');
  };

  const tabItems = [
    {
      key: 'dashboard',
      label: (
        <span>
          <BarChartOutlined />
          Dashboard
        </span>
      ),
      children: (
        <div style={{ padding: '16px 0' }}>
          {/* Quota Overview */}
          <Row gutter={[16, 16]}>
            <Col xs={24} lg={12}>
              <QuotaProgress onUpgradeClick={handleUpgradeClick} />
            </Col>
            <Col xs={24} lg={12}>
              <UsageByModel />
            </Col>
          </Row>

          {/* Usage Chart */}
          <div style={{ marginTop: 16 }}>
            <UsageChart />
          </div>

          {/* Usage by Conversation */}
          <div style={{ marginTop: 16 }}>
            <UsageByConversation />
          </div>
        </div>
      ),
    },
    {
      key: 'profile',
      label: (
        <span>
          <UserOutlined />
          Profile
        </span>
      ),
      children: (
        <Card title="Profile Information" style={{ maxWidth: 600, marginTop: 24 }}>
          <Form layout="vertical" onFinish={onFinish}>
            <Form.Item
              label="Email"
              initialValue={user?.email}
            >
              <Input disabled defaultValue={user?.email} />
            </Form.Item>

            <Form.Item
              label="Username"
              initialValue={user?.username}
            >
              <Input disabled defaultValue={user?.username} />
            </Form.Item>

            <Form.Item>
              <Button type="primary" htmlType="submit">
                Save Changes
              </Button>
            </Form.Item>
          </Form>
        </Card>
      ),
    },
    {
      key: 'preferences',
      label: (
        <span>
          <SettingOutlined />
          Preferences
        </span>
      ),
      children: (
        <Card title="Application" style={{ maxWidth: 600, marginTop: 24 }}>
          <Form layout="vertical" initialValues={{ appName: APP_NAME }}>
            <Form.Item
              name="appName"
              label="Application Name"
            >
              <Input disabled />
            </Form.Item>

            <Form.Item
              name="theme"
              label="Theme"
            >
              <Input disabled placeholder="Coming soon" />
            </Form.Item>
          </Form>
        </Card>
      ),
    },
  ];

  return (
    <div>
      <Title level={3}>Settings</Title>
      <Text type="secondary">Manage your account, view usage, and application preferences</Text>

      <Divider />

      <Tabs
        activeKey={activeTab}
        onChange={setActiveTab}
        items={tabItems}
        size="large"
      />
    </div>
  );
};

export default SettingsPage;
