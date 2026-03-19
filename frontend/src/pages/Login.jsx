import { useState } from 'react';
import { Form, Input, Button, Typography, message, Divider } from 'antd';
import { UserOutlined, LockOutlined } from '@ant-design/icons';
import { useNavigate, Link } from 'react-router-dom';
import { GoogleLogin } from '@react-oauth/google';
import useAuthStore from '../store/authStore';
import { APP_NAME } from '../constants';

const { Title, Text } = Typography;

const LoginPage = () => {
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();
  const { login, loginWithGoogle } = useAuthStore();
  
  const onFinish = async (values) => {
    setLoading(true);
    try {
      await login(values);
      message.success('Login successful!');
      navigate('/chat');
    } catch (error) {
      message.error(error.message || 'Login failed. Please check your credentials.');
    } finally {
      setLoading(false);
    }
  };

  const handleGoogleSuccess = async (credentialResponse) => {
    setLoading(true);
    try {
      // credentialResponse.credential is the JWT ID token
      await loginWithGoogle(credentialResponse.credential);
      message.success('Google login successful!');
      navigate('/chat');
    } catch (error) {
      message.error(error.message || 'Google login failed.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{ display: 'flex', minHeight: '100vh', backgroundColor: '#f0f2f5' }}>
      {/* Left side hero */}
      <div 
        style={{ 
          flex: 1, 
          display: 'flex', 
          flexDirection: 'column', 
          justifyContent: 'center', 
          padding: '40px',
          background: 'linear-gradient(135deg, #1890ff 0%, #0050b3 100%)',
          color: 'white'
        }}
      >
        <div style={{ maxWidth: 480, margin: '0 auto' }}>
          <Title level={1} style={{ color: 'white', marginBottom: 24 }}>Welcome back to {APP_NAME}</Title>
          <Text style={{ color: 'rgba(255, 255, 255, 0.8)', fontSize: '18px', display: 'block', marginBottom: 32 }}>
            Your intelligent assistant for generating complex SQL queries from natural language. Sign in to continue your work.
          </Text>
        </div>
      </div>

      {/* Right side form */}
      <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '40px', backgroundColor: 'white' }}>
        <div style={{ width: '100%', maxWidth: '400px' }}>
          <div style={{ textAlign: 'center', marginBottom: '32px' }}>
            <Title level={2}>Sign In</Title>
            <Text type="secondary">Welcome back! Please enter your details.</Text>
          </div>
          
          <div style={{ display: 'flex', justifyContent: 'center', marginBottom: '24px' }}>
            <GoogleLogin
              onSuccess={handleGoogleSuccess}
              onError={() => message.error('Google Sign-In failed.')}
              useOneTap
              theme="outline"
              size="large"
              width="400"
            />
          </div>

          <Divider plain><Text type="secondary">or</Text></Divider>

          <Form
            name="login"
            onFinish={onFinish}
            autoComplete="off"
            size="large"
            layout="vertical"
          >
            <Form.Item
              name="email"
              rules={[
                { required: true, message: 'Please input your email!' },
                { type: 'email', message: 'Please enter a valid email!' },
              ]}
            >
              <Input 
                prefix={<UserOutlined />} 
                placeholder="Email address" 
              />
            </Form.Item>
            
            <Form.Item
              name="password"
              rules={[{ required: true, message: 'Please input your password!' }]}
            >
              <Input.Password 
                prefix={<LockOutlined />} 
                placeholder="Password" 
              />
            </Form.Item>
            
            <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: '24px' }}>
              <Link to="/forgot-password">Forgot password?</Link>
            </div>

            <Form.Item>
              <Button type="primary" htmlType="submit" loading={loading} block size="large">
                Sign In
              </Button>
            </Form.Item>
            
            <div style={{ textAlign: 'center' }}>
              <Text type="secondary">Don't have an account? </Text>
              <Link to="/register">Create an account</Link>
            </div>
          </Form>
        </div>
      </div>
    </div>
  );
};

export default LoginPage;
