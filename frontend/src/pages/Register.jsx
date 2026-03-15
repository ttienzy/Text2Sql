import { useState } from 'react';
import { Form, Input, Button, Card, Typography, message } from 'antd';
import { UserOutlined, LockOutlined, MailOutlined } from '@ant-design/icons';
import { useNavigate, Link } from 'react-router-dom';
import axiosInstance from '../api/axios';
import { APP_NAME } from '../constants';

const { Title, Text } = Typography;

const RegisterPage = () => {
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();
  
  const onFinish = async (values) => {
    setLoading(true);
    try {
      await axiosInstance.post('/api/auth/register', values);
      message.success('Registration successful! Please login.');
      navigate('/login');
    } catch (error) {
      message.error(error.response?.data?.message || 'Registration failed. Please try again.');
    } finally {
      setLoading(false);
    }
  };
  
  return (
    <Card style={{ boxShadow: '0 4px 12px rgba(0,0,0,0.15)' }}>
      <div style={{ textAlign: 'center', marginBottom: 24 }}>
        <Title level={2} style={{ marginBottom: 8 }}>{APP_NAME}</Title>
        <Text type="secondary">Create your account</Text>
      </div>
      
      <Form
        name="register"
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
            prefix={<MailOutlined />} 
            placeholder="Email" 
          />
        </Form.Item>
        
        <Form.Item
          name="username"
          rules={[{ required: true, message: 'Please input your username!' }]}
        >
          <Input 
            prefix={<UserOutlined />} 
            placeholder="Username" 
          />
        </Form.Item>
        
        <Form.Item
          name="password"
          rules={[
            { required: true, message: 'Please input your password!' },
            { min: 6, message: 'Password must be at least 6 characters!' },
          ]}
        >
          <Input.Password 
            prefix={<LockOutlined />} 
            placeholder="Password" 
          />
        </Form.Item>
        
        <Form.Item
          name="confirmPassword"
          dependencies={['password']}
          rules={[
            { required: true, message: 'Please confirm your password!' },
            ({ getFieldValue }) => ({
              validator(_, value) {
                if (!value || getFieldValue('password') === value) {
                  return Promise.resolve();
                }
                return Promise.reject(new Error('Passwords do not match!'));
              },
            }),
          ]}
        >
          <Input.Password 
            prefix={<LockOutlined />} 
            placeholder="Confirm Password" 
          />
        </Form.Item>
        
        <Form.Item>
          <Button type="primary" htmlType="submit" loading={loading} block>
            Register
          </Button>
        </Form.Item>
        
        <div style={{ textAlign: 'center' }}>
          <Text type="secondary">Already have an account? </Text>
          <Link to="/login">Sign in</Link>
        </div>
      </Form>
    </Card>
  );
};

export default RegisterPage;
