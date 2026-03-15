import React from 'react';
import { Steps, Typography, Space, Spin, Alert } from 'antd';
import {
  RocketOutlined,
  BuildOutlined,
  ExperimentOutlined,
  DatabaseOutlined,
  CheckCircleOutlined,
  CloseCircleOutlined,
  QuestionCircleOutlined,
  LoadingOutlined,
} from '@ant-design/icons';
import { ConnectionState, AgentStepEventType } from '../../hooks/useAgentQuery';

const { Text } = Typography;

/**
 * Step configuration for the progress display
 */
const stepConfig = [
  { key: 'tool_selection', title: 'Tool Selection', icon: <BuildOutlined /> },
  { key: 'tool_execution', title: 'Tool Execution', icon: <ExperimentOutlined /> },
  { key: 'sql_generation', title: 'SQL Generation', icon: <DatabaseOutlined /> },
  { key: 'sql_execution', title: 'SQL Execution', icon: <DatabaseOutlined /> },
];

/**
 * Get current step index based on event type
 * @param {string} category - The step category
 * @returns {number} - Step index (0-3) or -1 for special states
 */
const getStepIndex = (category) => {
  switch (category) {
    case 'tool_selection':
      return 0;
    case 'tool_execution':
      return 1;
    case 'analysis':
    case 'sql_generation':
      return 2;
    case 'sql_execution':
      return 3;
    case 'completed':
      return 3;
    case 'error':
    case 'clarification':
      return -1;
    default:
      return -1;
  }
};

/**
 * Get status based on event type
 * @param {string} category - The step category
 * @param {string} connState - The connection state
 * @returns {string} - 'process', 'finish', 'error', or 'wait'
 */
const getStatus = (category, connState) => {
  if (connState === ConnectionState.ERROR) {
    return 'error';
  }
  if (connState === ConnectionState.CONNECTING || connState === ConnectionState.STREAMING) {
    return 'process';
  }
  switch (category) {
    case 'completed':
      return 'finish';
    case 'error':
      return 'error';
    case 'clarification':
      return 'wait';
    default:
      return 'process';
  }
};

/**
 * AgentProgress - Component to display agent processing progress
 * 
 * @param {Object} props
 * @param {Object} props.currentStep - Current step information from useAgentQuery
 * @param {string} props.connectionState - Current connection state
 * @param {string} props.error - Error message if any
 */
const AgentProgress = ({ currentStep, connectionState, error }) => {
  const isConnecting = connectionState === ConnectionState.CONNECTING || 
                       connectionState === ConnectionState.STREAMING;
  const isError = connectionState === ConnectionState.ERROR;
  
  // Calculate current step index
  const currentIndex = currentStep ? getStepIndex(currentStep.category) : -1;
  
  // Determine overall status
  const status = getStatus(currentStep?.category, connectionState);

  // Show error state
  if (isError && error) {
    return (
      <div
        style={{
          display: 'flex',
          flexDirection: 'row',
          alignItems: 'flex-start',
          marginBottom: 16,
        }}
      >
        <Spin indicator={<LoadingOutlined style={{ fontSize: 24 }} spin />} />
        <div style={{ marginLeft: 12, flex: 1 }}>
          <Alert
            type="error"
            message="Error"
            description={error}
            showIcon
          />
        </div>
      </div>
    );
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'flex-start',
        marginBottom: 16,
      }}
    >
      {/* Avatar placeholder */}
      <div
        style={{
          width: 40,
          height: 40,
          borderRadius: '50%',
          backgroundColor: '#52c41a',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          marginRight: 8,
          flexShrink: 0,
        }}
      >
        <Spin indicator={<LoadingOutlined style={{ fontSize: 20, color: '#fff' }} spin />} />
      </div>
      
      <div
        style={{
          backgroundColor: '#ffffff',
          padding: '12px 16px',
          borderRadius: 8,
          border: '1px solid #e8e8e8',
          boxShadow: '0 1px 2px rgba(0,0,0,0.05)',
          maxWidth: '80%',
          flex: 1,
        }}
      >
        {/* Progress Steps */}
        <Steps
          current={currentIndex >= 0 ? currentIndex : 0}
          status={status}
          size="small"
          items={stepConfig.map((step, index) => ({
            title: step.title,
            icon: index === currentIndex && isConnecting ? <LoadingOutlined spin /> : step.icon,
          }))}
        />
        
        {/* Current Step Details */}
        {currentStep && (
          <div style={{ marginTop: 16 }}>
            <Space direction="vertical" size={4} style={{ width: '100%' }}>
              {/* Step name */}
              <div>
                <Text strong style={{ fontSize: 14 }}>
                  {currentStep.stepName}
                </Text>
              </div>
              
              {/* Step number */}
              {currentStep.stepNumber && currentStep.maxSteps && (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  Step {currentStep.stepNumber} of {currentStep.maxSteps}
                </Text>
              )}
              
              {/* Thought/Reasoning */}
              {currentStep.thought && (
                <div style={{ 
                  marginTop: 8, 
                  padding: '8px 12px', 
                  backgroundColor: '#f5f5f5', 
                  borderRadius: 4,
                  borderLeft: '3px solid #1890ff',
                }}>
                  <Text style={{ fontSize: 13, color: '#595959' }}>
                    {currentStep.thought}
                  </Text>
                </div>
              )}
              
              {/* Tool info */}
              {currentStep.toolName && (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  Tool: <code style={{ backgroundColor: '#f0f0f0', padding: '2px 6px', borderRadius: 4 }}>
                    {currentStep.toolName}
                  </code>
                </Text>
              )}
              
              {/* SQL Preview */}
              {currentStep.sqlGenerated && (
                <div style={{ marginTop: 8 }}>
                  <Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
                    Generated SQL:
                  </Text>
                  <pre style={{ 
                    margin: 0, 
                    padding: '8px 12px', 
                    backgroundColor: '#f5f5f5', 
                    borderRadius: 4,
                    fontSize: 12,
                    overflow: 'auto',
                    maxHeight: 100,
                  }}>
                    {currentStep.sqlGenerated}
                  </pre>
                </div>
              )}
              
              {/* Row count */}
              {currentStep.rowCount !== undefined && currentStep.rowCount !== null && (
                <Text type="secondary" style={{ fontSize: 12 }}>
                  {currentStep.rowCount} row{currentStep.rowCount !== 1 ? 's' : ''} returned
                </Text>
              )}
              
              {/* Final Answer */}
              {currentStep.answer && (
                <div style={{ marginTop: 8 }}>
                  <Text style={{ fontSize: 14 }}>
                    {currentStep.answer}
                  </Text>
                </div>
              )}
            </Space>
          </div>
        )}
        
        {/* Connecting state */}
        {isConnecting && !currentStep && (
          <div style={{ marginTop: 8 }}>
            <Text type="secondary">Connecting to agent...</Text>
          </div>
        )}
      </div>
    </div>
  );
};

export default AgentProgress;
