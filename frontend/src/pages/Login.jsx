import { useState } from 'react';
import { Form, Input, Button, Card, Typography, message } from 'antd';
import { UserOutlined, LockOutlined } from '@ant-design/icons';
import { useNavigate, Link } from 'react-router-dom';
import useAuthStore from '../store/authStore';
import { APP_NAME } from '../constants';

const { Title, Text } = Typography;

const LoginPage = () => {
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();
  const { login } = useAuthStore();
  
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
  
  return (
    <Card style={{ boxShadow: '0 4px 12px rgba(0,0,0,0.15)' }}>
      <div style={{ textAlign: 'center', marginBottom: 24 }}>
        <Title level={2} style={{ marginBottom: 8 }}>{APP_NAME}</Title>
        <Text type="secondary">Sign in to your account</Text>
      </div>
      
      <Form
        name="login"
        onFinish={onFinish}
        autoComplete="off"
        size="large"
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
            placeholder="Email" 
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
        
        <Form.Item>
          <Button type="primary" htmlType="submit" loading={loading} block>
            Sign In
          </Button>
        </Form.Item>
        
        <div style={{ textAlign: 'center' }}>
          <Text type="secondary">Don't have an account? </Text>
          <Link to="/register">Register now</Link>
        </div>
      </Form>
    </Card>
  );
};

export default LoginPage;
