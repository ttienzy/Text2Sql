/**
 * ConnectionSteps - 3-step wizard for setting up database connections
 * Step 1: Input - Enter database information
 * Step 2: Test - Test the connection
 * Step 3: Sync - Synchronize schema
 */
import { useState, useCallback } from 'react';
import {
  Steps,
  Card,
  Button,
  Space,
  Result,
  Spin,
  Typography,
  Alert,
  Divider,
} from 'antd';
import {
  DatabaseOutlined,
  CheckCircleOutlined,
  SyncOutlined,
  LoadingOutlined,
  RightOutlined,
  LeftOutlined,
  ReloadOutlined,
} from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import ConnectionForm from './ConnectionForm';
import useConnectionStore from '../../store/connectionStore';

const { Title, Text, Paragraph } = Typography;

/**
 * Step configuration
 */
const STEPS = [
  { key: 1, title: 'Input', icon: <DatabaseOutlined /> },
  { key: 2, title: 'Test', icon: <CheckCircleOutlined /> },
  { key: 3, title: 'Sync', icon: <SyncOutlined /> },
];

/**
 * ConnectionSteps Component
 * @param {Object} props
 * @param {Object} props.initialConnection - Existing connection for editing
 * @param {boolean} props.isEditing - Whether in edit mode
 * @param {Function} props.onComplete - Callback when setup is complete
 */
const ConnectionSteps = ({ initialConnection, isEditing = false, onComplete }) => {
  const navigate = useNavigate();
  const [currentStep, setCurrentStep] = useState(1);
  const [formData, setFormData] = useState(null);
  const [testResult, setTestResult] = useState(null);
  const [syncResult, setSyncResult] = useState(null);

  const {
    createConnection,
    updateConnection,
    testConnection,
    syncSchema,
    isLoading,
    isTesting,
    isSyncing,
    error,
  } = useConnectionStore();

  // Handle form submission (Step 1)
  const handleFormSubmit = useCallback(
    async (values) => {
      setFormData(values);
      setCurrentStep(2);
    },
    []
  );

  // Handle test connection (Step 2)
  const handleTestConnection = useCallback(async () => {
    try {
      const connectionId = initialConnection?.id;
      const testData = connectionId ? { id: connectionId, ...formData } : formData;
      
      const result = await testConnection(testData);
      setTestResult(result);

      if (result.success) {
        setCurrentStep(3);
      }
    } catch (err) {
      setTestResult({
        success: false,
        errorMessage: err.message || 'Connection test failed',
      });
    }
  }, [formData, initialConnection, testConnection]);

  // Handle skip test and go directly to sync
  const handleSkipTest = useCallback(() => {
    setTestResult({ success: true });
    setCurrentStep(3);
  }, []);

  // Handle schema sync (Step 3)
  const handleSyncSchema = useCallback(async () => {
    try {
      // First create or update the connection if it's new
      let connectionId = initialConnection?.id;
      
      if (!connectionId && formData) {
        const newConnection = await createConnection(formData);
        connectionId = newConnection.id;
      } else if (connectionId && formData) {
        await updateConnection(connectionId, formData);
      }

      // Then sync the schema
      const result = await syncSchema(connectionId);
      setSyncResult(result);
    } catch (err) {
      setSyncResult({
        success: false,
        errorMessage: err.message || 'Schema sync failed',
      });
    }
  }, [formData, initialConnection, createConnection, updateConnection, syncSchema]);

  // Handle complete and go to chat
  const handleComplete = useCallback(() => {
    if (onComplete) {
      onComplete();
    } else {
      navigate('/chat');
    }
  }, [navigate, onComplete]);

  // Handle back
  const handleBack = useCallback(() => {
    if (currentStep > 1) {
      setCurrentStep(currentStep - 1);
    }
  }, [currentStep]);

  // Handle retry
  const handleRetry = useCallback(() => {
    setTestResult(null);
    setSyncResult(null);
  }, []);

  // Render Step 1: Input
  const renderStepInput = () => (
    <Card>
      <Title level={4}>Database Connection Details</Title>
      <Paragraph type="secondary">
        Enter your database information to create a new connection.
      </Paragraph>
      <ConnectionForm
        initialValues={initialConnection}
        isEditing={isEditing}
        onSubmit={handleFormSubmit}
        isTesting={isTesting}
        loading={isLoading}
      />
    </Card>
  );

  // Render Step 2: Test
  const renderStepTest = () => (
    <Card>
      <div style={{ textAlign: 'center', padding: '24px 0' }}>
        <Title level={4}>Test Connection</Title>
        <Paragraph type="secondary">
          Testing connection to your database...
        </Paragraph>

        {isTesting ? (
          <Spin size="large" indicator={<LoadingOutlined spin />} />
        ) : testResult ? (
          testResult.success ? (
            <Result
              status="success"
              title="Connection Successful!"
              subTitle={`Connected in ${testResult.latencyMs}ms`}
              extra={[
                <Text key="version">
                  Server Version: {testResult.serverVersion}
                </Text>,
              ]}
            />
          ) : (
            <Result
              status="error"
              title="Connection Failed"
              subTitle={testResult.errorMessage}
              extra={[
                <Button
                  key="retry"
                  icon={<ReloadOutlined />}
                  onClick={handleRetry}
                >
                  Try Again
                </Button>,
              ]}
            />
          )
        ) : (
          <Space direction="vertical" size="large">
            <Alert
              message="Ready to Test"
              description="Click the button below to test your database connection."
              type="info"
              showIcon
            />
            <Button
              type="primary"
              size="large"
              icon={<CheckCircleOutlined />}
              onClick={handleTestConnection}
            >
              Test Connection
            </Button>
          </Space>
        )}

        {testResult?.success && (
          <div style={{ marginTop: 24 }}>
            <Button onClick={handleSkipTest}>
              Skip Test & Continue to Sync
            </Button>
          </div>
        )}

        {error && (
          <Alert
            message="Error"
            description={error}
            type="error"
            showIcon
            style={{ marginTop: 16 }}
          />
        )}
      </div>
    </Card>
  );

  // Render Step 3: Sync
  const renderStepSync = () => (
    <Card>
      <div style={{ textAlign: 'center', padding: '24px 0' }}>
        <Title level={4}>Synchronize Schema</Title>
        <Paragraph type="secondary">
          Syncing database schema for better query understanding...
        </Paragraph>

        {isSyncing ? (
          <Spin size="large" indicator={<LoadingOutlined spin />} />
        ) : syncResult ? (
          syncResult.success !== false ? (
            <Result
              status="success"
              title="Schema Synced Successfully!"
              subTitle="Your database schema is now ready for natural language queries."
              extra={[
                <Text key="tables">
                  {syncResult.tableCount} tables, {syncResult.columnCount} columns
                </Text>,
              ]}
            />
          ) : (
            <Result
              status="error"
              title="Schema Sync Failed"
              subTitle={syncResult.errorMessage}
              extra={[
                <Button
                  key="retry"
                  icon={<ReloadOutlined />}
                  onClick={handleRetry}
                >
                  Try Again
                </Button>,
              ]}
            />
          )
        ) : (
          <Space direction="vertical" size="large">
            <Alert
              message="Ready to Sync"
              description="Click the button below to synchronize your database schema. This will allow the AI to understand your database structure."
              type="info"
              showIcon
            />
            <Button
              type="primary"
              size="large"
              icon={<SyncOutlined />}
              onClick={handleSyncSchema}
            >
              Sync Schema Now
            </Button>
          </Space>
        )}

        {syncResult?.success !== false && syncResult && (
          <div style={{ marginTop: 24 }}>
            <Button type="primary" onClick={handleComplete}>
              Go to Chat
            </Button>
          </div>
        )}

        {error && (
          <Alert
            message="Error"
            description={error}
            type="error"
            showIcon
            style={{ marginTop: 16 }}
          />
        )}
      </div>
    </Card>
  );

  // Render step content
  const renderStepContent = () => {
    switch (currentStep) {
      case 1:
        return renderStepInput();
      case 2:
        return renderStepTest();
      case 3:
        return renderStepSync();
      default:
        return null;
    }
  };

  return (
    <div>
      {/* Steps */}
      <Steps
        current={currentStep - 1}
        items={STEPS.map((step) => ({
          title: step.title,
          icon: step.icon,
        }))}
        style={{ marginBottom: 24 }}
      />

      {/* Step Content */}
      <div style={{ minHeight: 400 }}>{renderStepContent()}</div>

      {/* Navigation */}
      {currentStep > 1 && currentStep < 3 && (
        <div style={{ marginTop: 24, textAlign: 'left' }}>
          <Button onClick={handleBack} icon={<LeftOutlined />}>
            Back
          </Button>
        </div>
      )}
    </div>
  );
};

export default ConnectionSteps;
