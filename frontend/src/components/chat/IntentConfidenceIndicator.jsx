import React from 'react';
import { Tag, Tooltip } from 'antd';

/**
 * Intent Confidence Indicator Component
 * Shows the classification confidence with color coding
 * 
 * @param {Object} props
 * @param {number|null} props.confidence - Confidence score (0-1)
 * @param {string} props.intent - Detected intent type
 * @param {boolean} props.showLabel - Whether to show the label
 * @param {boolean} props.compact - Compact mode for inline display
 */
const IntentConfidenceIndicator = ({
    confidence,
    intent,
    showLabel = true,
    compact = false
}) => {
    // Don't render if no confidence
    if (confidence === null || confidence === undefined) {
        return null;
    }

    // Get color based on confidence level
    const getConfidenceColor = () => {
        if (confidence >= 0.8) return 'green';  // High confidence
        if (confidence >= 0.6) return 'orange'; // Medium confidence
        return 'red';                           // Low confidence
    };

    // Get label based on confidence
    const getConfidenceLabel = () => {
        if (confidence >= 0.8) return 'High';
        if (confidence >= 0.6) return 'Medium';
        return 'Low';
    };

    // Format percentage
    const percentage = Math.round(confidence * 100);

    // Get intent display name
    const getIntentDisplayName = () => {
        if (!intent) return 'Unknown';
        
        const intentNames = {
            'Query': 'Query',
            'Insert': 'Insert',
            'Update': 'Update',
            'DdlIndex': 'DDL (Index)',
            'DdlProcedure': 'DDL (Procedure)',
            'DdlAlter': 'DDL (Alter)',
            'DdlView': 'DDL (View)',
            'Forbidden': 'Forbidden',
            'OffTopic': 'Off-topic',
            'Unknown': 'Unknown'
        };
        
        return intentNames[intent] || intent;
    };

    const color = getConfidenceColor();
    const label = getConfidenceLabel();

    // Compact mode - just show tag
    if (compact) {
        return (
            <Tooltip title={`${label} confidence (${percentage}%)`}>
                <Tag 
                    color={color}
                    style={{ 
                        margin: 0,
                        fontSize: '12px'
                    }}
                >
                    {percentage}%
                </Tag>
            </Tooltip>
        );
    }

    // Full mode - show detailed indicator
    return (
        <div style={{ 
            display: 'inline-flex', 
            alignItems: 'center', 
            gap: '8px',
            padding: '4px 8px',
            backgroundColor: color === 'green' ? '#f6ffed' : 
                           color === 'orange' ? '#fff7e6' : '#fff1f0',
            borderRadius: '4px',
            border: `1px solid ${color === 'green' ? '#b7eb8f' : 
                                        color === 'orange' ? '#ffd591' : '#ffccc7'}`
        }}>
            {showLabel && (
                <span style={{ 
                    fontSize: '12px', 
                    color: '#666',
                    fontWeight: 500
                }}>
                    Intent:
                </span>
            )}
            <Tag 
                color={color}
                style={{ margin: 0 }}
            >
                {getIntentDisplayName()}
            </Tag>
            <span style={{ 
                fontSize: '12px', 
                color: '#888',
                fontWeight: 600
            }}>
                {percentage}%
            </span>
            <Tooltip title={`${label} confidence - System is ${label === 'High' ? 'confident' : 'less confident'} in this classification`}>
                <span style={{
                    cursor: 'help',
                    fontSize: '14px',
                    color: '#999'
                }}>
                    ℹ️
                </span>
            </Tooltip>
        </div>
    );
};

/**
 * Low Confidence Warning Component
 * Shows warning when classification confidence is low
 */
export const LowConfidenceWarning = ({ 
    confidence, 
    threshold = 0.6,
    onRetry,
    onOverride 
}) => {
    if (confidence === null || confidence >= threshold) {
        return null;
    }

    const percentage = Math.round(confidence * 100);

    return (
        <div style={{
            marginTop: '8px',
            padding: '8px 12px',
            backgroundColor: '#fff1f0',
            border: '1px solid #ffccc7',
            borderRadius: '4px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between'
        }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <span style={{ fontSize: '16px' }}>⚠️</span>
                <span style={{ color: '#cf1322', fontSize: '13px' }}>
                    Low confidence ({percentage}%). 
                    The system may have misclassified your intent.
                </span>
            </div>
            <div style={{ display: 'flex', gap: '8px' }}>
                {onRetry && (
                    <button 
                        onClick={onRetry}
                        style={{
                            padding: '4px 12px',
                            backgroundColor: '#fff',
                            border: '1px solid #d9d9d9',
                            borderRadius: '4px',
                            cursor: 'pointer',
                            fontSize: '12px'
                        }}
                    >
                        Retry
                    </button>
                )}
                {onOverride && (
                    <button 
                        onClick={onOverride}
                        style={{
                            padding: '4px 12px',
                            backgroundColor: '#1890ff',
                            border: 'none',
                            borderRadius: '4px',
                            color: '#fff',
                            cursor: 'pointer',
                            fontSize: '12px'
                        }}
                    >
                        Override
                    </button>
                )}
            </div>
        </div>
    );
};

export default IntentConfidenceIndicator;
