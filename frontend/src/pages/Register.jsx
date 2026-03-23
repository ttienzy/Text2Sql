import { useState } from 'react';
import { Form, Input, Button, Typography, message, Divider } from 'antd';
import { UserOutlined, LockOutlined, MailOutlined } from '@ant-design/icons';
import { useNavigate, Link } from 'react-router-dom';
import { GoogleLogin } from '@react-oauth/google';
import axiosInstance from '../api/axios';
import useAuthStore from '../store/authStore';
import { APP_NAME } from '../constants';
import styles from './Register.module.css';

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
      await loginWithGoogle(credentialResponse.credential);
      message.success('Google signup successful!');
      navigate('/chat');
    } catch (error) {
      message.error(error.message || 'Google Sign-Up failed.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className={styles.container}>
      {/* Left side - Hero */}
      <div className={styles.heroSection}>
        <div className={styles.heroContent}>
          <Title level={1} className={styles.heroTitle}>Get started with {APP_NAME}</Title>
          <Text className={styles.heroText}>
            Join thousands of professionals translating natural language to precise SQL queries
            instantly. Sign up for free today.
          </Text>
        </div>
      </div>

      {/* Right side - Form */}
      <div className={styles.formSection}>
        <div className={styles.formCard}>
          <div className={styles.header}>
            <Title level={2} className={styles.title}>Create an Account</Title>
            <Text type="secondary">Sign up today to boost your productivity.</Text>
          </div>

          <div className={styles.googleWrapper}>
            <GoogleLogin
              onSuccess={handleGoogleSuccess}
              onError={() => message.error('Google Sign-Up failed.')}
              theme="outline"
              size="large"
              width="340"
              text="signup_with"
            />
          </div>

          <Divider plain>
            <Text type="secondary">or</Text>
          </Divider>

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

            <div className={styles.footer}>
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
