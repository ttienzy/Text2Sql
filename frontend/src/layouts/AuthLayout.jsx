import { Layout } from 'antd';
import { Outlet } from 'react-router-dom';

const { Content } = Layout;

const AuthLayout = () => {
  return (
    <Layout style={{ minHeight: '100vh' }}>
      <Content style={{ 
        display: 'flex', 
        justifyContent: 'center', 
        alignItems: 'center',
        background: 'linear-gradient(135deg, #1890ff 0%, #096dd9 100%)',
      }}>
        <div style={{ 
          width: '100%', 
          maxWidth: 400, 
          padding: 24,
        }}>
          <Outlet />
        </div>
      </Content>
    </Layout>
  );
};

export default AuthLayout;
