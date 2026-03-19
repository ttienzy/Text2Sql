import { useState } from 'react';
import { Form, Input, Button, Typography, message, Divider } from 'antd';
import { UserOutlined, LockOutlined, MailOutlined } from '@ant-design/icons';
import { useNavigate, Link } from 'react-router-dom';
import { GoogleLogin } from '@react-oauth/google';
import axiosInstance from '../api/axios';
import useAuthStore from '../store/authStore';
import { APP_NAME } from '../constants';

const { Title, Text } = Typography;

const RegisterPage = () => {
  const [loading, setLoading] = useState(false);
  const navigate = useNavigate();
  const { loginWithGoogle } = useAuthStore();
  
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

  const handleGoogleSuccess = async (credentialResponse) => {
    setLoading(true);
    try {
      // credentialResponse.credential is the JWT ID token
      await loginWithGoogle(credentialResponse.credential);
      message.success('Google signup/login successful!');
      navigate('/chat');
    } catch (error) {
      message.error(error.message || 'Google Sign-Up failed.');
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
          <Title level={1} style={{ color: 'white', marginBottom: 24 }}>Get started with {APP_NAME}</Title>
          <Text style={{ color: 'rgba(255, 255, 255, 0.8)', fontSize: '18px', display: 'block', marginBottom: 32 }}>
            Join thousands of professionals translating natural language to precise SQL queries instantly. Sign up for free today.
          </Text>
        </div>
      </div>

      {/* Right side form */}
      <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '40px', backgroundColor: 'white' }}>
        <div style={{ width: '100%', maxWidth: '400px' }}>
          <div style={{ textAlign: 'center', marginBottom: '32px' }}>
            <Title level={2}>Create an Account</Title>
            <Text type="secondary">Sign up today to boost your productivity.</Text>
          </div>

          <div style={{ display: 'flex', justifyContent: 'center', marginBottom: '24px' }}>
            <GoogleLogin
              onSuccess={handleGoogleSuccess}
              onError={() => message.error('Google Sign-Up failed.')}
              theme="outline"
              size="large"
              width="400"
              text="signup_with"
            />
          </div>

          <Divider plain><Text type="secondary">or</Text></Divider>
          
          <Form
            name="register"
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
                prefix={<MailOutlined />} 
                placeholder="Email address" 
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
              hasFeedback
            >
              <Input.Password 
                prefix={<LockOutlined />} 
                placeholder="Password" 
              />
            </Form.Item>
            
            <Form.Item
              name="confirmPassword"
              dependencies={['password']}
              hasFeedback
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
              <Button type="primary" htmlType="submit" loading={loading} block size="large">
                Create Account
              </Button>
            </Form.Item>
            
            <div style={{ textAlign: 'center' }}>
              <Text type="secondary">Already have an account? </Text>
              <Link to="/login">Sign in here</Link>
            </div>
          </Form>
        </div>
      </div>
    </div>
  );
};

export default RegisterPage;
