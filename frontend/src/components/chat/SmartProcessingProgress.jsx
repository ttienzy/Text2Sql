import React from 'react';
import EnhancedProcessingProgress from './EnhancedProcessingProgress';
import ProcessingProgress from './ProcessingProgress';
import { useStreamingProgress } from '../../hooks/useStreamingProgress';

/**
 * SmartProcessingProgress - Intelligent progress component that can switch between modes
 * - Enhanced: Detailed ReAct agent simulation
 * - Simple: Basic progress display
 * - Streaming: Real-time updates from API (future)
 */
const SmartProcessingProgress = ({
    question,
    isVisible = true,
    connectionName = 'Database',
    mode = 'enhanced', // 'enhanced', 'simple', 'streaming'
    onStepComplete = null,
    streamingEndpoint = null,
}) => {
    // Use streaming hook for real-time updates
    const {
        currentStep: streamingStep,
        progress: streamingProgress,
        elapsedTime,
        isComplete,
    } = useStreamingProgress(isVisible && mode === 'streaming', question);

    // Don't render if not visible
    if (!isVisible) return null;

    // Render based on mode
    switch (mode) {
        case 'streaming':
            // Future: Real streaming implementation
            return (
                <EnhancedProcessingProgress
                    question={question}
                    isVisible={isVisible}
                    connectionName={connectionName}
                    onStepComplete={onStepComplete}
                    // Pass streaming data when available
                    streamingData={{
                        currentStep: streamingStep,
                        progress: streamingProgress,
                        elapsedTime,
                        isComplete,
                    }}
                />
            );

        case 'simple':
            return (
                <ProcessingProgress
                    question={question}
                    isVisible={isVisible}
                />
            );

        case 'enhanced':
        default:
            return (
                <EnhancedProcessingProgress
                    question={question}
                    isVisible={isVisible}
                    connectionName={connectionName}
                    onStepComplete={onStepComplete}
                />
            );
    }
};

export default SmartProcessingProgress;