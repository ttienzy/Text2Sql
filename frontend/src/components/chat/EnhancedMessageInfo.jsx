import React, { useState } from 'react';
import {
    Collapse,
    Typography,
    Tag,
    Space,
    Button,
    Divider,
} from 'antd';
import {
    InfoCircleOutlined,
    BulbOutlined,
    HistoryOutlined,
    CaretRightOutlined,
} from '@ant-design/icons';

const { Panel } = Collapse;
const { Text, Paragraph } = Typography;

/**
 * EnhancedMessageInfo - Component for displaying additional message information
 * Shows processing steps, suggested queries, correction history, etc.
 */
const EnhancedMessageInfo = ({ message, onSuggestedQueryClick }) => {
    const [activeKey, setActiveKey] = useState([]);

    if (!message || message.role === 'user') {
        return null;
    }

    const {
        processingSteps = [],
        suggestedQueries = [],
        correctionHistory = [],
        wasCorrected = false,
        queryExplanation,
        success,
    } = message;

    // Parse JSON strings if needed with error handling
    const parsedProcessingSteps = (() => {
        try {
            if (Array.isArray(processingSteps)) {
                return processingSteps;
            }
            if (typeof processingSteps === 'string' && processingSteps.trim()) {
                return JSON.parse(processingSteps);
            }
            return [];
        } catch (e) {
            console.warn('Failed to parse processingSteps:', e);
            return [];
        }
    })();

    const parsedSuggestedQueries = (() => {
        try {
            if (Array.isArray(suggestedQueries)) {
                return suggestedQueries;
            }
            if (typeof suggestedQueries === 'string' && suggestedQueries.trim()) {
                return JSON.parse(suggestedQueries);
            }
            return [];
        } catch (e) {
            console.warn('Failed to parse suggestedQueries:', e);
            return [];
        }
    })();

    const parsedCorrectionHistory = (() => {
        try {
            if (Array.isArray(correctionHistory)) {
                return correctionHistory;
            }
            if (typeof correctionHistory === 'string' && correctionHistory.trim()) {
                return JSON.parse(correctionHistory);
            }
            return [];
        } catch (e) {
            console.warn('Failed to parse correctionHistory:', e);
            return [];
        }
    })();

    // Don't show if no additional info
    if (!parsedSuggestedQueries.length && !parsedCorrectionHistory.length && !queryExplanation) {
        return null;
    }

    const handleSuggestedQueryClick = (query) => {
        if (onSuggestedQueryClick) {
            onSuggestedQueryClick(query);
        }
    };

    return (
        <div style={{ marginTop: 12 }}>
            <Collapse
                activeKey={activeKey}
                onChange={setActiveKey}
                size="small"
                ghost
                expandIcon={({ isActive }) => <CaretRightOutlined rotate={isActive ? 90 : 0} />}
            >
                {/* Processing Steps have been removed as they are now handled by the dynamic ThinkingIndicator */}

                {/* Query Explanation */}
                {queryExplanation && (
                    <Panel
                        header={
                            <Space>
                                <InfoCircleOutlined style={{ color: '#52c41a' }} />
                                <Text strong>Query Explanation</Text>
                            </Space>
                        }
                        key="explanation"
                    >
                        <Paragraph style={{ marginBottom: 0, paddingLeft: 16 }}>
                            <Text>{queryExplanation}</Text>
                        </Paragraph>
                    </Panel>
                )}

                {/* Suggested Queries */}
                {parsedSuggestedQueries.length > 0 && (
                    <Panel
                        header={
                            <Space>
                                <BulbOutlined style={{ color: '#faad14' }} />
                                <Text strong>Suggested Follow-up Queries</Text>
                                <Tag color="orange" size="small">
                                    {parsedSuggestedQueries.length} suggestions
                                </Tag>
                            </Space>
                        }
                        key="suggestions"
                    >
                        <div style={{ paddingLeft: 16 }}>
                            <Space direction="vertical" size="small" style={{ width: '100%' }}>
                                {parsedSuggestedQueries.map((query, index) => (
                                    <Button
                                        key={index}
                                        type="link"
                                        size="small"
                                        onClick={() => handleSuggestedQueryClick(query)}
                                        style={{
                                            textAlign: 'left',
                                            padding: '4px 8px',
                                            height: 'auto',
                                            whiteSpace: 'normal',
                                            fontSize: 12,
                                        }}
                                    >
                                        {query}
                                    </Button>
                                ))}
                            </Space>
                        </div>
                    </Panel>
                )}

                {/* Correction History */}
                {parsedCorrectionHistory.length > 0 && (
                    <Panel
                        header={
                            <Space>
                                <HistoryOutlined style={{ color: wasCorrected ? '#ff4d4f' : '#8c8c8c' }} />
                                <Text strong>Correction History</Text>
                                <Tag color={wasCorrected ? 'error' : 'default'} size="small">
                                    {parsedCorrectionHistory.length} attempts
                                </Tag>
                            </Space>
                        }
                        key="corrections"
                    >
                        <div style={{ paddingLeft: 16 }}>
                            {parsedCorrectionHistory.map((correction, index) => (
                                <div key={index} style={{ marginBottom: 12 }}>
                                    <Text strong style={{ fontSize: 12 }}>
                                        Attempt {correction.attemptNumber}:
                                    </Text>
                                    <div style={{ marginTop: 4, marginBottom: 4 }}>
                                        <Text type="secondary" style={{ fontSize: 11 }}>
                                            {correction.reasoning}
                                        </Text>
                                    </div>
                                    {correction.error && (
                                        <div style={{
                                            padding: '4px 8px',
                                            backgroundColor: '#fff2f0',
                                            borderRadius: 4,
                                            marginTop: 4,
                                        }}>
                                            <Text type="danger" style={{ fontSize: 11 }}>
                                                Error: {correction.error}
                                            </Text>
                                        </div>
                                    )}
                                    {index < parsedCorrectionHistory.length - 1 && <Divider style={{ margin: '8px 0' }} />}
                                </div>
                            ))}
                        </div>
                    </Panel>
                )}
            </Collapse>
        </div>
    );
};

export default EnhancedMessageInfo;