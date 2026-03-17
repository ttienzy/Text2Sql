import React, { useState, useEffect } from 'react';
import {
    Steps,
    Typography,
    Space,
    Spin,
    Progress,
    Card,
    Divider,
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
} from '@ant-design/icons';

const { Text, Paragraph } = Typography;

/**
 * ProcessingProgress - Enhanced loading component that shows detailed processing steps
 * Simulates the ReAct agent processing steps with realistic timing
 */
const ProcessingProgress = ({ question, isVisible = true }) => {
    const [currentStep, setCurrentStep] = useState(0);
    const [currentSubStep, setCurrentSubStep] = useState('');
    const [progress, setProgress] = useState(0);
    const [elapsedTime, setElapsedTime] = useState(0);

    // Define processing steps with realistic descriptions
    const steps = [
        {
            title: 'Understanding Question',
            icon: <BulbOutlined />,
            description: 'Analyzing your natural language query...',
            subSteps: [
                'Parsing question structure',
                'Identifying key entities',
                'Understanding intent',
            ],
            duration: 800,
        },
        {
            title: 'Schema Analysis',
            icon: <SearchOutlined />,
            description: 'Exploring database schema and relationships...',
            subSteps: [
                'Retrieving relevant tables',
                'Analyzing relationships',
                'Mapping entities to schema',
            ],
            duration: 1200,
        },
        {
            title: 'SQL Generation',
            icon: <CodeOutlined />,
            description: 'Generating optimized SQL query...',
            subSteps: [
                'Building query structure',
                'Adding joins and filters',
                'Optimizing performance',
            ],
            duration: 1000,
        },
        {
            title: 'Query Execution',
            icon: <DatabaseOutlined />,
            description: 'Executing query against database...',
            subSteps: [
                'Validating SQL syntax',
                'Executing query',
                'Processing results',
            ],
            duration: 600,
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

    // Progress simulation
    useEffect(() => {
        if (!isVisible) return;

        let stepIndex = 0;
        let subStepIndex = 0;
        let stepStartTime = Date.now();

        const progressTimer = setInterval(() => {
            const currentStepConfig = steps[stepIndex];
            if (!currentStepConfig) {
                clearInterval(progressTimer);
                return;
            }

            const elapsed = Date.now() - stepStartTime;
            const stepProgress = Math.min(elapsed / currentStepConfig.duration, 1);

            // Update current substep
            const subStepProgress = stepProgress * currentStepConfig.subSteps.length;
            const currentSubStepIndex = Math.floor(subStepProgress);

            if (currentSubStepIndex < currentStepConfig.subSteps.length) {
                setCurrentSubStep(currentStepConfig.subSteps[currentSubStepIndex]);
            }

            // Update overall progress
            const overallProgress = ((stepIndex + stepProgress) / steps.length) * 100;
            setProgress(overallProgress);

            // Move to next step
            if (stepProgress >= 1) {
                if (stepIndex < steps.length - 1) {
                    stepIndex++;
                    setCurrentStep(stepIndex);
                    stepStartTime = Date.now();
                } else {
                    clearInterval(progressTimer);
                    setProgress(100);
                }
            }
        }, 100);

        return () => clearInterval(progressTimer);
    }, [isVisible]);

    if (!isVisible) return null;

    const formatTime = (ms) => {
        const seconds = Math.floor(ms / 1000);
        const milliseconds = ms % 1000;
        return `${seconds}.${Math.floor(milliseconds / 100)}s`;
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
                    maxWidth: '80%',
                    boxShadow: '0 2px 8px rgba(0,0,0,0.1)',
                }}
                bodyStyle={{ padding: '16px' }}
            >
                {/* Header */}
                <div style={{ marginBottom: 16 }}>
                    <Space>
                        <RobotOutlined style={{ color: '#52c41a', fontSize: 16 }} />
                        <Text strong style={{ fontSize: 16 }}>
                            Processing your question...
                        </Text>
                        <Text type="secondary" style={{ fontSize: 12 }}>
                            {formatTime(elapsedTime)}
                        </Text>
                    </Space>
                </div>

                {/* Question Preview */}
                <div style={{ marginBottom: 16 }}>
                    <Text type="secondary" style={{ fontSize: 12 }}>
                        Question:
                    </Text>
                    <Paragraph
                        style={{
                            margin: '4px 0 0 0',
                            padding: '8px 12px',
                            backgroundColor: '#f5f5f5',
                            borderRadius: 4,
                            fontSize: 13,
                        }}
                        ellipsis={{ rows: 2, expandable: false }}
                    >
                        {question}
                    </Paragraph>
                </div>

                {/* Overall Progress */}
                <div style={{ marginBottom: 16 }}>
                    <Progress
                        percent={Math.round(progress)}
                        size="small"
                        status="active"
                        strokeColor={{
                            '0%': '#108ee9',
                            '100%': '#87d068',
                        }}
                    />
                </div>

                {/* Steps */}
                <Steps
                    current={currentStep}
                    size="small"
                    direction="vertical"
                    items={steps.map((step, index) => ({
                        title: step.title,
                        description: index === currentStep ? step.description : '',
                        icon: index === currentStep ? <LoadingOutlined spin /> :
                            index < currentStep ? <CheckCircleOutlined style={{ color: '#52c41a' }} /> :
                                step.icon,
                        status: index < currentStep ? 'finish' :
                            index === currentStep ? 'process' : 'wait',
                    }))}
                />

                {/* Current Sub-step */}
                {currentSubStep && (
                    <>
                        <Divider style={{ margin: '12px 0' }} />
                        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                            <ToolOutlined style={{ color: '#1890ff', fontSize: 12 }} />
                            <Text type="secondary" style={{ fontSize: 12 }}>
                                {currentSubStep}
                            </Text>
                            <Spin size="small" />
                        </div>
                    </>
                )}

                {/* Tips */}
                <div style={{ marginTop: 16, padding: '8px 12px', backgroundColor: '#f0f5ff', borderRadius: 4 }}>
                    <Text type="secondary" style={{ fontSize: 11 }}>
                        💡 Tip: Complex queries may take longer to process. The AI is analyzing your database schema to generate the most accurate SQL.
                    </Text>
                </div>
            </Card>
        </div>
    );
};

export default ProcessingProgress;