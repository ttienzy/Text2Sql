import React, { useState, useEffect, useRef, useMemo } from 'react';
import { 
  Typography, 
  Button, 
  Space, 
  Radio, 
  Input, 
  Form, 
  Alert, 
  Card,
} from 'antd';
import { 
  QuestionCircleOutlined, 
  CheckCircleOutlined, 
  CloseCircleOutlined,
  WarningOutlined,
  FormOutlined,
  MessageOutlined,
} from '@ant-design/icons';
import { ClarificationType } from '../../constants/clarification';

const { Text, Title } = Typography;

/**
 * ClarificationRequest - Component to display and handle clarification requests
 * 
 * @param {Object} props
 * @param {Object} props.clarificationData - The clarification request data from the event
 * @param {Function} props.onAnswer - Callback when user provides an answer
 * @param {boolean} props.isLoading - Whether the answer is being submitted
 */
const ClarificationRequest = ({ clarificationData, onAnswer, isLoading }) => {
  const [form] = Form.useForm();
  const [freeText, setFreeText] = useState('');
  const [countdown, setCountdown] = useState(0);
  const timerRef = useRef(null);

  // Extract data from event metadata
  const {
    sessionId,
    clarificationType,
    question,
    options = [],
    timeoutSeconds = 300,
    sqlQuery,
    originalQuestion,
  } = clarificationData || {};

  // Memoize timeout value to avoid issues with useEffect
  const timeoutValue = useMemo(() => timeoutSeconds, [timeoutSeconds]);

  // Cleanup timer on unmount
  useEffect(() => {
    return () => {
      if (timerRef.current) {
        clearInterval(timerRef.current);
      }
    };
  }, []);

  // Start countdown timer when timeout value changes
  useEffect(() => {
    // Clear existing timer
    if (timerRef.current) {
      clearInterval(timerRef.current);
    }
    
    if (timeoutValue > 0) {
      setCountdown(timeoutValue);
      timerRef.current = setInterval(() => {
        setCountdown((prev) => {
          if (prev <= 1) {
            if (timerRef.current) {
              clearInterval(timerRef.current);
            }
            return 0;
          }
          return prev - 1;
        });
      }, 1000);
    }
    
    return () => {
      if (timerRef.current) {
        clearInterval(timerRef.current);
      }
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [timeoutValue]);

  // Format countdown as MM:SS
  const formatTime = (seconds) => {
    const mins = Math.floor(seconds / 60);
    const secs = seconds % 60;
    return `${mins}:${secs.toString().padStart(2, '0')}`;
  };

  // Handle form submission based on type
  const handleSubmit = (values = {}) => {
    let answer = {};
    
    switch (clarificationType) {
      case ClarificationType.DML_CONFIRMATION:
        answer = { confirmed: values.confirmed };
        break;
      case ClarificationType.AMBIGUOUS_QUESTION:
        if (options.length > 0) {
          answer = { selectedOption: values.selectedOption };
        } else {
          answer = { answer: values.freeText };
        }
        break;
      case ClarificationType.MISSING_PARAMETERS:
        answer = { answer: JSON.stringify(values.parameters) };
        break;
      case ClarificationType.RESULT_INTERPRETATION:
        answer = { answer: values.continue || 'continue' };
        break;
      default:
        answer = { answer: values.freeText };
    }
    
    onAnswer(sessionId, answer);
  };

  // Render DML Confirmation
  const renderDmlConfirmation = () => (
    <div>
      <Alert
        type="warning"
        icon={<WarningOutlined />}
        message="Confirmation Required"
        description="The agent wants to execute a data modification query. Please review and confirm."
        style={{ marginBottom: 16 }}
      />
      
      {sqlQuery && (
        <Card size="small" style={{ marginBottom: 16, backgroundColor: '#fff7e6' }}>
          <Text strong style={{ display: 'block', marginBottom: 8 }}>SQL Query:</Text>
          <pre style={{ 
            margin: 0, 
            padding: '8px 12px', 
            backgroundColor: '#f5f5f5', 
            borderRadius: 4,
            fontSize: 12,
            overflow: 'auto',
            maxHeight: 150,
          }}>
            {sqlQuery}
          </pre>
        </Card>
      )}
      
      <Form form={form} layout="vertical" onFinish={handleSubmit}>
        <Form.Item 
          name="confirmed" 
          rules={[{ required: true, message: 'Please confirm or reject the query' }]}
        >
          <Radio.Group buttonStyle="solid">
            <Radio.Button value={true}>
              <CheckCircleOutlined /> Confirm & Execute
            </Radio.Button>
            <Radio.Button value={false}>
              <CloseCircleOutlined /> Reject
            </Radio.Button>
          </Radio.Group>
        </Form.Item>
        
        <Form.Item>
          <Button 
            type="primary" 
            htmlType="submit" 
            loading={isLoading}
          >
            Submit
          </Button>
        </Form.Item>
      </Form>
    </div>
  );

  // Render Ambiguous Question
  const renderAmbiguousQuestion = () => {
    const hasOptions = options && options.length > 0;
    
    return (
      <div>
        <Alert
          type="info"
          icon={<QuestionCircleOutlined />}
          message="Clarification Needed"
          description={question}
          style={{ marginBottom: 16 }}
        />
        
        <Form form={form} layout="vertical" onFinish={handleSubmit}>
          {hasOptions ? (
            <Form.Item 
              name="selectedOption" 
              rules={[{ required: true, message: 'Please select an option' }]}
            >
              <Radio.Group>
                <Space direction="vertical" style={{ width: '100%' }}>
                  {options.map((option, index) => (
                    <Radio key={index} value={option} style={{ width: '100%' }}>
                      {option}
                    </Radio>
                  ))}
                </Space>
              </Radio.Group>
            </Form.Item>
          ) : (
            <Form.Item 
              name="freeText" 
              rules={[{ required: true, message: 'Please provide an answer' }]}
            >
              <Input.TextArea 
                placeholder="Please clarify your question..."
                rows={3}
                value={freeText}
                onChange={(e) => setFreeText(e.target.value)}
              />
            </Form.Item>
          )}
          
          <Form.Item>
            <Button 
              type="primary" 
              htmlType="submit" 
              loading={isLoading}
            >
              Submit
            </Button>
          </Form.Item>
        </Form>
      </div>
    );
  };

  // Render Missing Parameters
  const renderMissingParameters = () => {
    // Extract parameter names from question or context
    const paramMatches = question?.match(/\{(\w+)\}/g) || [];
    const params = paramMatches.map(p => p.replace(/[{}]/g, ''));
    
    return (
      <div>
        <Alert
          type="warning"
          icon={<FormOutlined />}
          message="Missing Information"
          description={question}
          style={{ marginBottom: 16 }}
        />
        
        <Form form={form} layout="vertical" onFinish={handleSubmit}>
          {params.length > 0 ? (
            params.map((param) => (
              <Form.Item
                key={param}
                name={['parameters', param]}
                label={param}
                rules={[{ required: true, message: `Please enter ${param}` }]}
              >
                <Input placeholder={`Enter ${param}`} />
              </Form.Item>
            ))
          ) : (
            <Form.Item 
              name="parameters"
              rules={[{ required: true, message: 'Please provide the required information' }]}
            >
              <Input.TextArea 
                placeholder="Please provide the missing information..."
                rows={3}
              />
            </Form.Item>
          )}
          
          <Form.Item>
            <Button 
              type="primary" 
              htmlType="submit" 
              loading={isLoading}
            >
              Submit
            </Button>
          </Form.Item>
        </Form>
      </div>
    );
  };

  // Render Result Interpretation
  const renderResultInterpretation = () => (
    <div>
      <Alert
        type="info"
        icon={<MessageOutlined />}
        message="Result Interpretation"
        description={question}
        style={{ marginBottom: 16 }}
      />
      
      <Form form={form} layout="vertical" onFinish={handleSubmit}>
        <Form.Item name="continue">
          <Button 
            type="primary" 
            htmlType="submit" 
            loading={isLoading}
          >
            Continue
          </Button>
        </Form.Item>
      </Form>
    </div>
  );

  // Render Timeout
  const renderTimeout = () => (
    <Alert
      type="error"
      message="Request Timed Out"
      description="The clarification request has expired. Please try your query again."
      showIcon
    />
  );

  // Main render
  const renderContent = () => {
    switch (clarificationType) {
      case ClarificationType.DML_CONFIRMATION:
        return renderDmlConfirmation();
      case ClarificationType.AMBIGUOUS_QUESTION:
        return renderAmbiguousQuestion();
      case ClarificationType.MISSING_PARAMETERS:
        return renderMissingParameters();
      case ClarificationType.RESULT_INTERPRETATION:
        return renderResultInterpretation();
      case ClarificationType.TIMEOUT:
        return renderTimeout();
      default:
        return (
          <Alert
            type="warning"
            message="Unknown Clarification Type"
            description="Please refresh the page and try again."
          />
        );
    }
  };

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'flex-start',
        marginBottom: 16,
      }}
    >
      {/* Avatar */}
      <div
        style={{
          width: 40,
          height: 40,
          borderRadius: '50%',
          backgroundColor: '#faad14',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          marginRight: 8,
          flexShrink: 0,
        }}
      >
        <QuestionCircleOutlined style={{ fontSize: 20, color: '#fff' }} />
      </div>
      
      {/* Content */}
      <div
        style={{
          backgroundColor: '#ffffff',
          padding: '12px 16px',
          borderRadius: 8,
          border: '1px solid #faad14',
          boxShadow: '0 1px 2px rgba(0,0,0,0.05)',
          maxWidth: '80%',
          flex: 1,
        }}
      >
        {/* Header with countdown */}
        <div style={{ 
          display: 'flex', 
          justifyContent: 'space-between', 
          alignItems: 'center',
          marginBottom: 12,
          borderBottom: '1px solid #f0f0f0',
          paddingBottom: 8,
        }}>
          <Title level={5} style={{ margin: 0, color: '#fa8c16' }}>
            Clarification Required
          </Title>
          {countdown > 0 && (
            <Text type="secondary" style={{ fontSize: 12 }}>
              Time remaining: {formatTime(countdown)}
            </Text>
          )}
        </div>
        
        {/* Original question if any */}
        {originalQuestion && (
          <Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 8 }}>
            Original question: {originalQuestion}
          </Text>
        )}
        
        {renderContent()}
      </div>
    </div>
  );
};

export default ClarificationRequest;
