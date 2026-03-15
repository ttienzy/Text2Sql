import { useState, useEffect } from 'react';
import { Layout, Menu, Avatar, Dropdown, Space, Typography, Badge, Button, Tooltip } from 'antd';
import { 
  DatabaseOutlined, 
  MessageOutlined, 
  SettingOutlined, 
  LogoutOutlined,
  UserOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  SunOutlined,
  MoonOutlined,
  LeftOutlined,
  RightOutlined,
  InfoCircleOutlined,
} from '@ant-design/icons';
import { useNavigate, useLocation, Outlet } from 'react-router-dom';
import useAuthStore from '../store/authStore';
import useConnectionStore from '../store/connectionStore';

const { Header, Sider, Content } = Layout;
const { Text } = Typography;

// Default widths for the 3-column layout
const SIDEBAR_WIDTH = 280;
const INFOPANEL_WIDTH = 320;

const MainLayout = () => {
  const [collapsed, setCollapsed] = useState(false);
  const [sidebarVisible, setSidebarVisible] = useState(true);
  const [infoPanelVisible, setInfoPanelVisible] = useState(true);
  const [theme, setTheme] = useState('light');
  
  const navigate = useNavigate();
  const location = useLocation();
  
  const { user, logout } = useAuthStore();
  const { activeConnection, fetchConnections } = useConnectionStore();
  
  useEffect(() => {
    // Fetch connections on mount - only once
    // eslint-disable-next-line react-hooks/exhaustive-deps
    fetchConnections();
  }, []); // Empty dependency array - fetch only once on mount
  
  // Toggle theme
  const toggleTheme = () => {
    setTheme(theme === 'light' ? 'dark' : 'light');
    // In a real app, you'd also update the Ant Design theme config
  };

  const handleMenuClick = ({ key }) => {
    if (key === 'logout') {
      logout();
      navigate('/login');
    } else {
      navigate(key);
    }
  };
  
  const toggleSidebar = () => setSidebarVisible(!sidebarVisible);
  const toggleInfoPanel = () => setInfoPanelVisible(!infoPanelVisible);
  
  const menuItems = [
    {
      key: '/chat',
      icon: <MessageOutlined />,
      label: 'Chat',
    },
    {
      key: '/connections',
      icon: <DatabaseOutlined />,
      label: 'Connections',
    },
    {
      key: '/settings',
      icon: <SettingOutlined />,
      label: 'Settings',
    },
  ];
  
  const userMenuItems = [
    {
      key: 'profile',
      icon: <UserOutlined />,
      label: 'Profile',
    },
    {
      type: 'divider',
    },
    {
      key: 'logout',
      icon: <LogoutOutlined />,
      label: 'Logout',
      danger: true,
    },
  ];
  
  // Check if we're on the chat page to show 3-column layout
  const isChatPage = location.pathname === '/chat';
  
  return (
    <Layout style={{ minHeight: '100vh' }}>
      {/* Left Sidebar */}
      {sidebarVisible && (
        <Sider 
          trigger={null} 
          width={SIDEBAR_WIDTH}
          theme="light"
          style={{ 
            borderRight: '1px solid #f0f0f0',
            overflow: 'auto',
            height: '100vh',
            position: 'fixed',
            left: 0,
            top: 0,
            bottom: 0,
            zIndex: 100,
          }}
        >
          <div style={{ 
            height: 64, 
            display: 'flex', 
            alignItems: 'center', 
            justifyContent: 'center',
            borderBottom: '1px solid #f0f0f0',
          }}>
            <Text style={{ fontSize: 16, fontWeight: 'bold', color: '#1890ff' }}>
              TextToSQL Agent
            </Text>
          </div>
          
          <Menu
            theme="light"
            mode="inline"
            selectedKeys={[location.pathname]}
            items={menuItems}
            onClick={handleMenuClick}
            style={{ borderRight: 0 }}
          />
          
          {activeConnection && (
            <div style={{ 
              padding: '16px',
              borderTop: '1px solid #f0f0f0',
              marginTop: 'auto',
            }}>
              <Text type="secondary" style={{ fontSize: 12 }}>
                Active Connection
              </Text>
              <div style={{ marginTop: 4, display: 'flex', alignItems: 'center' }}>
                <DatabaseOutlined style={{ marginRight: 8, color: '#52c41a' }} />
                <Text ellipsis style={{ flex: 1 }}>
                  {activeConnection.name}
                </Text>
              </div>
            </div>
          )}
        </Sider>
      )}
      
      <Layout style={{ 
        marginLeft: sidebarVisible ? SIDEBAR_WIDTH : 0,
        transition: 'margin-left 0.2s',
      }}>
        <Header style={{ 
          padding: '0 24px', 
          background: '#fff', 
          display: 'flex', 
          alignItems: 'center',
          justifyContent: 'space-between',
          borderBottom: '1px solid #f0f0f0',
          height: 64,
        }}>
          <Space>
            <Tooltip title={sidebarVisible ? 'Hide Sidebar' : 'Show Sidebar'}>
              <Button 
                type="text" 
                icon={sidebarVisible ? <LeftOutlined /> : <RightOutlined />} 
                onClick={toggleSidebar}
              />
            </Tooltip>
            {collapsed ? (
              <MenuUnfoldOutlined onClick={() => setCollapsed(false)} style={{ fontSize: 18 }} />
            ) : (
              <MenuFoldOutlined onClick={() => setCollapsed(true)} style={{ fontSize: 18 }} />
            )}
          </Space>
          
          <Space size="middle">
            {activeConnection && (
              <Badge status="success" text={activeConnection.name} />
            )}
            
            <Tooltip title={theme === 'light' ? 'Dark Mode' : 'Light Mode'}>
              <Button 
                type="text" 
                icon={theme === 'light' ? <MoonOutlined /> : <SunOutlined />} 
                onClick={toggleTheme}
              />
            </Tooltip>
            
            <Tooltip title={infoPanelVisible ? 'Hide Info Panel' : 'Show Info Panel'}>
              <Button 
                type="text" 
                icon={<InfoCircleOutlined />} 
                onClick={toggleInfoPanel}
              />
            </Tooltip>
            
            <Dropdown menu={{ items: userMenuItems }} placement="bottomRight">
              <Space style={{ cursor: 'pointer' }}>
                <Avatar icon={<UserOutlined />} />
                <Text>{user?.email || user?.username || 'User'}</Text>
              </Space>
            </Dropdown>
          </Space>
        </Header>
        
        {/* Main Content Area */}
        <Layout style={{ 
          padding: isChatPage ? 0 : 24,
          background: isChatPage ? '#fff' : 'transparent',
        }}>
          {/* Chat Content with Info Panel */}
          {isChatPage ? (
            <Layout style={{ background: '#fff' }}>
              <Content style={{ 
                padding: 0,
                minHeight: 280,
                overflow: 'hidden',
              }}>
                <Outlet />
              </Content>
              
              {/* Right Info Panel */}
              {infoPanelVisible && (
                <Sider 
                  width={INFOPANEL_WIDTH}
                  theme="light"
                  style={{ 
                    borderLeft: '1px solid #f0f0f0',
                    overflow: 'auto',
                  }}
                >
                  <div style={{ padding: 16 }}>
                    <Text strong style={{ fontSize: 16 }}>Connection Info</Text>
                    
                    {activeConnection ? (
                      <div style={{ marginTop: 16 }}>
                        <div style={{ marginBottom: 8 }}>
                          <Text type="secondary">Database:</Text>
                          <div><Text>{activeConnection.databaseType || 'Unknown'}</Text></div>
                        </div>
                        <div style={{ marginBottom: 8 }}>
                          <Text type="secondary">Host:</Text>
                          <div><Text ellipsis>{activeConnection.host || 'N/A'}</Text></div>
                        </div>
                        <div style={{ marginBottom: 8 }}>
                          <Text type="secondary">Status:</Text>
                          <div>
                            <Badge status="success" text="Connected" />
                          </div>
                        </div>
                      </div>
                    ) : (
                      <div style={{ marginTop: 16 }}>
                        <Text type="secondary">No connection selected</Text>
                      </div>
                    )}
                  </div>
                </Sider>
              )}
            </Layout>
          ) : (
            <Content style={{ 
              margin: 0, 
              padding: 24, 
              background: '#fff', 
              borderRadius: 8,
              minHeight: 280,
            }}>
              <Outlet />
            </Content>
          )}
        </Layout>
      </Layout>
    </Layout>
  );
};

export default MainLayout;
