import React, { useState, useEffect } from 'react';
import {
    Steps,
    Typography,
    Space,
    Spin,
    Progress,
    Card,
    Divider,
    Tag,
    Tooltip,
} from 'antd';
import {
    RobotOutlined,
    DatabaseOutlined,
    SearchOutlined,
    CodeOutlined,
    CheckCircleOutlined,
    LoadingOutlined,
    BulbOutlined,
    ToolOutlined,
    ClockCircleOutlined,
    ThunderboltOutlined,
    EyeOutlined,
} from '@ant-design/icons';

const { Text, Paragraph } = Typography;

/**
 * EnhancedProcessingProgress - Advanced loading component with realistic ReAct agent simulation
 * Shows detailed processing steps that mirror the actual ReAct agent workflow
 */
const EnhancedProcessingProgress = ({
    question,
    isVisible = true,
    connectionName = 'Database',
    onStepComplete = null
}) => {
    const [currentStep, setCurrentStep] = useState(0);
    const [currentSubStep, setCurrentSubStep] = useState('');
    const [currentThought, setCurrentThought] = useState('');
    const [progress, setProgress] = useState(0);
    const [elapsedTime, setElapsedTime] = useState(0);
    const [completedSteps, setCompletedSteps] = useState([]);
    const [isThinking, setIsThinking] = useState(true);

    // ReAct Agent Steps - mirrors actual agent workflow
    const reactSteps = [
        {
            title: 'THINK: Analyzing Question',
            icon: <BulbOutlined />,
            description: 'Understanding the natural language query...',
            thoughts: [
                'I need to understand what the user is asking for',
                'Let me break down the key components of this question',
                'Identifying the main entities and relationships needed',
            ],
            subSteps: [
                'Parsing natural language',
                'Extracting key entities',
                'Understanding query intent',
                'Planning approach',
            ],
            duration: 1000,
            color: '#722ed1',
        },
        {
            title: 'ACT: Schema Exploration',
            icon: <SearchOutlined />,
            description: 'Exploring database schema and relationships...',
            thoughts: [
                'I need to find the relevant tables for this query',
                'Let me explore the database schema',
                'Checking table relationships and foreign keys',
            ],
            subSteps: [
                'Retrieving table schemas',
                'Analyzing relationships',
                'Mapping entities to tables',
                'Identifying join paths',
            ],
            duration: 1200,
            color: '#1890ff',
        },
        {
            title: 'OBSERVE: Schema Analysis',
            icon: <EyeOutlined />,
            description: 'Processing schema information...',
            thoughts: [
                'Found relevant tables and columns',
                'Understanding the data structure',
                'Planning the SQL query structure',
            ],
            subSteps: [
                'Processing table metadata',
                'Validating column types',
                'Planning query joins',
            ],
            duration: 800,
            color: '#52c41a',
        },
        {
            title: 'THINK: Query Planning',
            icon: <BulbOutlined />,
            description: 'Planning the SQL query structure...',
            thoughts: [
                'Now I understand the schema structure',
                'I can plan the optimal SQL query',
                'Considering performance and accuracy',
            ],
            subSteps: [
                'Designing query structure',
                'Planning joins and filters',
                'Optimizing for performance',
            ],
            duration: 900,
            color: '#722ed1',
        },
        {
            title: 'ACT: SQL Generation',
            icon: <CodeOutlined />,
            description: 'Generating optimized SQL query...',
            thoughts: [
                'Generating the SQL query based on my analysis',
                'Ensuring proper syntax and optimization',
                'Adding necessary joins and conditions',
            ],
            subSteps: [
                'Building SELECT clause',
                'Adding JOIN statements',
                'Applying WHERE conditions',
                'Optimizing query',
            ],
            duration: 1000,
            color: '#1890ff',
        },
        {
            title: 'ACT: Query Execution',
            icon: <DatabaseOutlined />,
            description: 'Executing query against database...',
            thoughts: [
                'Executing the generated SQL query',
                'Validating results',
                'Preparing response',
            ],
            subSteps: [
                'Validating SQL syntax',
                'Executing query',
                'Processing results',
                'Formatting response',
            ],
            duration: 700,
            color: '#1890ff',
        },
    ];

    // Timer for elapsed time
    useEffect(() => {
        if (!isVisible) return;

        const timer = setInterval(() => {
            setElapsedTime(prev => prev + 100);
        }, 100);

        return () => clearInterval(timer);
    }, [isVisible]);

    // Progress simulation with ReAct pattern
    useEffect(() => {
        if (!isVisible) return;

        let stepIndex = 0;
        let subStepIndex = 0;
        let thoughtIndex = 0;
        let stepStartTime = Date.now();

        const progressTimer = setInterval(() => {
            const currentStepConfig = reactSteps[stepIndex];
            if (!currentStepConfig) {
                clearInterval(progressTimer);
                return;
            }

            const elapsed = Date.now() - stepStartTime;
            const stepProgress = Math.min(elapsed / currentStepConfig.duration, 1);

            // Update thinking phase
            if (stepProgress < 0.3 && currentStepConfig.thoughts) {
                setIsThinking(true);
                const thoughtProgress = (stepProgress / 0.3) * currentStepConfig.thoughts.length;
                const currentThoughtIndex = Math.floor(thoughtProgress);
                if (currentThoughtIndex < currentStepConfig.thoughts.length) {
                    setCurrentThought(currentStepConfig.thoughts[currentThoughtIndex]);
                }
            } else {
                setIsThinking(false);
                setCurrentThought('');
            }

            // Update current substep
            const subStepProgress = stepProgress * currentStepConfig.subSteps.length;
            const currentSubStepIndex = Math.floor(subStepProgress);

            if (currentSubStepIndex < currentStepConfig.subSteps.length) {
                setCurrentSubStep(currentStepConfig.subSteps[currentSubStepIndex]);
            }

            // Update overall progress
            const overallProgress = ((stepIndex + stepProgress) / reactSteps.length) * 100;
            setProgress(overallProgress);

            // Move to next step
            if (stepProgress >= 1) {
                // Mark step as completed
                setCompletedSteps(prev => [...prev, stepIndex]);

                if (onStepComplete) {
                    onStepComplete(currentStepConfig.title, stepIndex);
                }

                if (stepIndex < reactSteps.length - 1) {
                    stepIndex++;
                    setCurrentStep(stepIndex);
                    stepStartTime = Date.now();
                } else {
                    clearInterval(progressTimer);
                    setProgress(100);
                    setIsThinking(false);
                }
            }
        }, 150);

        return () => clearInterval(progressTimer);
    }, [isVisible, onStepComplete]);

    if (!isVisible) return null;

    const formatTime = (ms) => {
        const seconds = Math.floor(ms / 1000);
        const milliseconds = ms % 1000;
        return `${seconds}.${Math.floor(milliseconds / 100)}s`;
    };

    const currentStepConfig = reactSteps[currentStep];

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

            {/* Progress Card */}
            <Card
                style={{
                    flex: 1,
                    maxWidth: '85%',
                    boxShadow: '0 4px 12px rgba(0,0,0,0.1)',
                    border: '1px solid #e8f4fd',
                }}
                bodyStyle={{ padding: '20px' }}
            >
                {/* Header */}
                <div style={{ marginBottom: 20 }}>
                    <Space size={12}>
                        <RobotOutlined style={{ color: '#52c41a', fontSize: 18 }} />
                        <Text strong style={{ fontSize: 16 }}>
                            ReAct Agent Processing
                        </Text>
                        <Tag color="processing" icon={<ThunderboltOutlined />}>
                            {connectionName}
                        </Tag>
                        <Tooltip title="Processing time">
                            <Space size={4} style={{ color: '#666', fontSize: 12 }}>
                                <ClockCircleOutlined />
                                <span>{formatTime(elapsedTime)}</span>
                            </Space>
                        </Tooltip>
                    </Space>
                </div>

                {/* Question Preview */}
                <div style={{ marginBottom: 20 }}>
                    <Text type="secondary" style={{ fontSize: 12, fontWeight: 500 }}>
                        QUESTION:
                    </Text>
                    <Paragraph
                        style={{
                            margin: '6px 0 0 0',
                            padding: '12px 16px',
                            backgroundColor: '#f8f9fa',
                            borderRadius: 6,
                            fontSize: 14,
                            borderLeft: '3px solid #1890ff',
                        }}
                        ellipsis={{ rows: 2, expandable: true }}
                    >
                        {question}
                    </Paragraph>
                </div>

                {/* Current Thought (THINK phase) */}
                {isThinking && currentThought && (
                    <div style={{ marginBottom: 16 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 8 }}>
                            <BulbOutlined style={{ color: '#722ed1', fontSize: 14 }} />
                            <Text strong style={{ fontSize: 13, color: '#722ed1' }}>
                                THINKING...
                            </Text>
                        </div>
                        <div style={{
                            padding: '10px 14px',
                            backgroundColor: '#f6f0ff',
                            borderRadius: 6,
                            borderLeft: '3px solid #722ed1',
                        }}>
                            <Text style={{ fontSize: 13, color: '#531dab', fontStyle: 'italic' }}>
                                "{currentThought}"
                            </Text>
                        </div>
                    </div>
                )}

                {/* Overall Progress */}
                <div style={{ marginBottom: 20 }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 8 }}>
                        <Text style={{ fontSize: 12, fontWeight: 500 }}>
                            PROGRESS
                        </Text>
                        <Text style={{ fontSize: 12, color: '#666' }}>
                            {Math.round(progress)}%
                        </Text>
                    </div>
                    <Progress
                        percent={Math.round(progress)}
                        size="small"
                        status="active"
                        strokeColor={{
                            '0%': '#722ed1',
                            '50%': '#1890ff',
                            '100%': '#52c41a',
                        }}
                        trailColor="#f0f0f0"
                    />
                </div>

                {/* Steps */}
                <Steps
                    current={currentStep}
                    size="small"
                    direction="vertical"
                    items={reactSteps.map((step, index) => ({
                        title: (
                            <span style={{ fontSize: 13, fontWeight: 500 }}>
                                {step.title}
                            </span>
                        ),
                        description: index === currentStep ? (
                            <Text type="secondary" style={{ fontSize: 12 }}>
                                {step.description}
                            </Text>
                        ) : null,
                        icon: index === currentStep ? (
                            <LoadingOutlined spin style={{ color: step.color }} />
                        ) : index < currentStep ? (
                            <CheckCircleOutlined style={{ color: '#52c41a' }} />
                        ) : (
                            React.cloneElement(step.icon, { style: { color: '#d9d9d9' } })
                        ),
                        status: index < currentStep ? 'finish' :
                            index === currentStep ? 'process' : 'wait',
                    }))}
                />

                {/* Current Sub-step */}
                {currentSubStep && !isThinking && (
                    <>
                        <Divider style={{ margin: '16px 0 12px 0' }} />
                        <div style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: 10,
                            padding: '8px 12px',
                            backgroundColor: '#f0f5ff',
                            borderRadius: 4,
                            border: '1px solid #d6e4ff',
                        }}>
                            <ToolOutlined style={{ color: currentStepConfig?.color || '#1890ff', fontSize: 12 }} />
                            <Text style={{ fontSize: 12, fontWeight: 500 }}>
                                {currentSubStep}
                            </Text>
                            <Spin size="small" />
                        </div>
                    </>
                )}

                {/* Performance Tip */}
                <div style={{
                    marginTop: 16,
                    padding: '10px 14px',
                    backgroundColor: '#f6ffed',
                    borderRadius: 4,
                    border: '1px solid #b7eb8f',
                }}>
                    <Text style={{ fontSize: 11, color: '#389e0d' }}>
                        💡 The ReAct agent uses reasoning and acting cycles to ensure accurate SQL generation.
                        Complex queries require more analysis time for optimal results.
                    </Text>
                </div>
            </Card>
        </div>
    );
};

export default EnhancedProcessingProgress;