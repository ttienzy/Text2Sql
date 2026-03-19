import { useState, useEffect } from 'react';
import { Typography, Card, Form, Input, Button, message, Divider, Tabs, Row, Col, Avatar, Tag, Space, Spin } from 'antd';
import {
  UserOutlined,
  AppstoreOutlined,
  BarChartOutlined,
  SettingOutlined,
  GoogleOutlined,
  CheckCircleOutlined
} from '@ant-design/icons';
import useAuthStore from '../store/authStore';
import { APP_NAME } from '../constants';
import {
  QuotaProgress,
  UsageChart,
  UsageByConversation,
  UsageByModel,
} from '../components/dashboard';

const { Title, Text } = Typography;

const SettingsPage = () => {
  const { user, fetchProfile } = useAuthStore();
  const [activeTab, setActiveTab] = useState('dashboard');
  
  const [profileData, setProfileData] = useState(null);
  const [loadingProfile, setLoadingProfile] = useState(false);

  useEffect(() => {
    const loadProfile = async () => {
      setLoadingProfile(true);
      try {
        const data = await fetchProfile();
        setProfileData(data);
      } catch (error) {
        console.error('Failed to load profile:', error);
      } finally {
        setLoadingProfile(false);
      }
    };
    
    // Load profile when User switches to the profile tab
    if (activeTab === 'profile') {
      loadProfile();
    }
  }, [activeTab, fetchProfile]);

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
        <div style={{ marginTop: 24 }}>
          {loadingProfile ? (
            <div style={{ textAlign: 'center', padding: '40px' }}><Spin size="large" /></div>
          ) : (
            <Row gutter={[24, 24]}>
              <Col xs={24} md={12}>
                <Card title="Profile Information" bordered={false} style={{ height: '100%' }}>
                  <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', marginBottom: 24 }}>
                    <Avatar 
                      size={100} 
                      src={profileData?.avatarUrl} 
                      icon={!profileData?.avatarUrl && <UserOutlined />} 
                      style={{ marginBottom: 16, backgroundColor: '#1890ff' }}
                    />
                    <Title level={4} style={{ margin: 0 }}>{profileData?.fullName || user?.username || 'User'}</Title>
                    <Text type="secondary">{profileData?.email || user?.email}</Text>
                  </div>
                  <Divider />
                  <Form layout="vertical" onFinish={onFinish}>
                    <Form.Item label="Email" initialValue={profileData?.email || user?.email}>
                      <Input disabled />
                    </Form.Item>
                    <Form.Item label="Username" initialValue={profileData?.fullName || user?.username}>
                      <Input disabled />
                    </Form.Item>
                  </Form>
                </Card>
              </Col>
              
              <Col xs={24} md={12}>
                <Card title="Linked Accounts" bordered={false} style={{ height: '100%' }}>
                  <Text type="secondary" style={{ display: 'block', marginBottom: 16 }}>
                    Connect external accounts to sign in easily.
                  </Text>
                  
                  <div style={{ 
                    display: 'flex', 
                    alignItems: 'center', 
                    justifyContent: 'space-between',
                    padding: '16px',
                    border: '1px solid #f0f0f0',
                    borderRadius: '8px'
                  }}>
                    <Space size="large">
                      <GoogleOutlined style={{ fontSize: '24px', color: '#DB4437' }} />
                      <div>
                        <Text strong style={{ display: 'block' }}>Google</Text>
                        <Text type="secondary" style={{ fontSize: '12px' }}>Sign in to TextToSql using your Google account</Text>
                      </div>
                    </Space>
                    
                    {profileData?.linkedProviders?.includes('Google') ? (
                      <Tag icon={<CheckCircleOutlined />} color="success">
                        Linked
                      </Tag>
                    ) : (
                      <Tag color="default">
                        Not Linked
                      </Tag>
                    )}
                  </div>
                </Card>
              </Col>
            </Row>
          )}
        </div>
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
