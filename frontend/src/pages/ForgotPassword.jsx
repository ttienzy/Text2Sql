import React, { useState } from 'react';
import { Card, Form, Input, Button, Typography, message, Space } from 'antd';
import { MailOutlined, LockOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { Link, useNavigate } from 'react-router-dom';
import useAuthStore from '../store/authStore';

const { Title, Text } = Typography;

const ForgotPassword = () => {
  const [step, setStep] = useState(1);
  const [email, setEmail] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  
  const { forgotPassword, resetPassword } = useAuthStore();
  const navigate = useNavigate();
  const [form] = Form.useForm();

  const handleSendCode = async (values) => {
    setIsLoading(true);
    try {
      await forgotPassword(values.email);
      setEmail(values.email);
      setStep(2);
      message.success('Password reset code sent to your email.');
    } catch (error) {
      message.error(error.response?.data?.error || 'Failed to send reset code');
    } finally {
      setIsLoading(false);
    }
  };

  const handleResetPassword = async (values) => {
    setIsLoading(true);
    try {
      await resetPassword(email, values.code, values.newPassword);
      message.success('Password has been reset successfully. Please login.');
      navigate('/login');
    } catch (error) {
      message.error(error.response?.data?.error || 'Failed to reset password');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div style={{ display: 'flex', minHeight: '100vh', backgroundColor: '#f0f2f5' }}>
      {/* Left side (same style as Login) */}
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
          <Title level={1} style={{ color: 'white', marginBottom: 24 }}>Account Recovery</Title>
          <Text style={{ color: 'rgba(255, 255, 255, 0.8)', fontSize: '18px', display: 'block', marginBottom: 32 }}>
            Get back access to your TextToSQL Agent with a quick email verification code.
          </Text>
        </div>
      </div>

      {/* Right side form */}
      <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '40px', backgroundColor: 'white' }}>
        <div style={{ width: '100%', maxWidth: '400px' }}>
          <div style={{ textAlign: 'center', marginBottom: '32px' }}>
            <Title level={2}>Forgot Password</Title>
            <Text type="secondary">
              {step === 1 ? "Enter your email to receive a reset code." : `Enter the 6-digit code sent to ${email}`}
            </Text>
          </div>

          {step === 1 ? (
            <Form layout="vertical" onFinish={handleSendCode} size="large">
              <Form.Item
                name="email"
                rules={[
                  { required: true, message: 'Please input your email!' },
                  { type: 'email', message: 'Please enter a valid email!' }
                ]}
              >
                <Input prefix={<MailOutlined />} placeholder="Email Address" />
              </Form.Item>

              <Form.Item>
                <Button type="primary" htmlType="submit" loading={isLoading} block size="large">
                  Send Reset Code
                </Button>
              </Form.Item>
              <div style={{ textAlign: 'center' }}>
                <Text type="secondary">Remember your password? <Link to="/login">Back to Login</Link></Text>
              </div>
            </Form>
          ) : (
            <Form form={form} layout="vertical" onFinish={handleResetPassword} size="large">
              <Form.Item
                name="code"
                rules={[
                  { required: true, message: 'Please input the 6-digit code!' },
                  { len: 6, message: 'Code must be exactly 6 characters!' }
                ]}
              >
                <Input prefix={<SafetyCertificateOutlined />} placeholder="6-digit reset code" maxLength={6} />
              </Form.Item>

              <Form.Item
                name="newPassword"
                rules={[
                  { required: true, message: 'Please input your new password!' },
                  { min: 6, message: 'Password must be at least 6 characters!' }
                ]}
                hasFeedback
              >
                <Input.Password prefix={<LockOutlined />} placeholder="New Password" />
              </Form.Item>

              <Form.Item
                name="confirmPassword"
                dependencies={['newPassword']}
                hasFeedback
                rules={[
                  { required: true, message: 'Please confirm your new password!' },
                  ({ getFieldValue }) => ({
                    validator(_, value) {
                      if (!value || getFieldValue('newPassword') === value) {
                        return Promise.resolve();
                      }
                      return Promise.reject(new Error('Passwords do not match!'));
                    },
                  }),
                ]}
              >
                <Input.Password prefix={<LockOutlined />} placeholder="Confirm New Password" />
              </Form.Item>

              <Form.Item>
                <Button type="primary" htmlType="submit" loading={isLoading} block size="large">
                  Reset Password
                </Button>
              </Form.Item>
              <div style={{ textAlign: 'center' }}>
                <Text type="secondary">
                  Didn't receive the code? <a onClick={() => setStep(1)} style={{ cursor: 'pointer' }}>Try again</a>
                </Text>
              </div>
            </Form>
          )}
        </div>
      </div>
    </div>
  );
};

export default ForgotPassword;
