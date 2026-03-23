import { useState } from 'react';
import { Form, Input, Button, Typography, message, Divider } from 'antd';
import { UserOutlined, LockOutlined } from '@ant-design/icons';
import { useNavigate, Link } from 'react-router-dom';
import { GoogleLogin } from '@react-oauth/google';
import useAuthStore from '../store/authStore';
import { APP_NAME } from '../constants';
import styles from './Login.module.css';

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
    <div className={styles.container}>
      {/* Left side - Hero */}
      <div className={styles.heroSection}>
        <div className={styles.heroContent}>
          <Title level={1} className={styles.heroTitle}>Welcome back to {APP_NAME}</Title>
          <Text className={styles.heroText}>
            Your intelligent assistant for generating complex SQL queries from natural language.
            Sign in to continue your work.
          </Text>
        </div>
      </div>

      {/* Right side - Form */}
      <div className={styles.formSection}>
        <div className={styles.formCard}>
          <div className={styles.header}>
            <Title level={2} className={styles.title}>Sign In</Title>
            <Text type="secondary">Welcome back! Please enter your details.</Text>
          </div>

          <div className={styles.googleWrapper}>
            <GoogleLogin
              onSuccess={handleGoogleSuccess}
              onError={() => message.error('Google Sign-In failed.')}
              useOneTap
              theme="outline"
              size="large"
              width="340"
            />
          </div>

          <Divider plain>
            <Text type="secondary">or</Text>
          </Divider>

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

            <div className={styles.forgotWrapper}>
              <Link to="/forgot-password">Forgot password?</Link>
            </div>

            <Form.Item>
              <Button type="primary" htmlType="submit" loading={loading} block size="large">
                Sign In
              </Button>
            </Form.Item>

            <div className={styles.footer}>
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
