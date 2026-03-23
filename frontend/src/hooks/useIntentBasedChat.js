import { useState, useCallback } from 'react';
import axios from '../api/axios';
import { useWriteOperation } from '../api/write';
import { useDDLOperation } from '../api/ddl';
import { API_ENDPOINTS, PIPELINE_TYPES } from '../constants/api';
import {
    UnifiedPipelineResponse,
    isQueryData,
    isWriteData,
    isDdlData,
    isForbiddenData,
    isRejectionData
} from '../types/responses';

/**
 * Unified hook for intent-based chat with automatic pipeline routing
 * Handles QUERY, WRITE, DDL, and FORBIDDEN operations
 * NOW USES: UnifiedPipelineResponse for all responses
 * 
 * @param {string} connectionId - Database connection ID
 * @param {string} conversationId - Conversation ID
 * @returns {Object} Hook state and actions
 */
export const useIntentBasedChat = (connectionId, conversationId) => {
    // State for different operation types
    const [queryResponse, setQueryResponse] = useState(null);
    const [forbiddenResult, setForbiddenResult] = useState(null);
    const [rejectionMessage, setRejectionMessage] = useState(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);
    const [currentPipeline, setCurrentPipeline] = useState(null);
    
    // ✅ NEW: Intent classification confidence
    const [intentConfidence, setIntentConfidence] = useState(null);

    // Hooks for WRITE and DDL operations
    const writeOp = useWriteOperation();
    const ddlOp = useDDLOperation();

    /**
     * Send message and handle automatic routing based on intent
     * @param {string} question - User's question
     * @returns {Promise<Object>} Response with type and data
     */
    const send = useCallback(async (question) => {
        if (!question?.trim()) {
            setError('Question cannot be empty');
            return { type: 'error', error: 'Question cannot be empty' };
        }

        if (!connectionId) {
            setError('Connection ID is required');
            return { type: 'error', error: 'Connection ID is required' };
        }

        setLoading(true);
        setError(null);
        setQueryResponse(null);
        setForbiddenResult(null);
        setRejectionMessage(null);
        setCurrentPipeline(null);
        setIntentConfidence(null); // ✅ Reset confidence
        writeOp.reset();
        ddlOp.reset();

        try {
            // Send to main agent endpoint - backend returns UnifiedPipelineResponse
            const response = await axios.post(API_ENDPOINTS.AGENT.PROCESS, {
                question,
                connectionId,
                conversationId
            });

            /** @type {UnifiedPipelineResponse} */
            const data = response.data;

            // ✅ Consistent access - no fallbacks needed
            const { pipeline, intent } = data;
            setCurrentPipeline(pipeline);
            
            // ✅ NEW: Extract confidence for UI display
            // Try multiple locations where confidence might be
            const confidence = data.execution?.classificationConfidence 
                || data.data?.confidence 
                || (intent && intent.confidence)
                || null;
            
            if (confidence !== null) {
                setIntentConfidence(confidence);
                console.debug('[IntentClassifier] Confidence:', confidence);
            }

            // Route based on pipeline type
            switch (pipeline) {
                case 'Forbidden': {
                    // FORBIDDEN operation - show alert with safe alternatives
                    if (isForbiddenData(data.data)) {
                        setForbiddenResult(data.data.result);
                    }
                    return {
                        type: 'forbidden',
                        data: data.data,
                        pipeline,
                        intent
                    };
                }

                case 'Write': {
                    // WRITE operation - show preview modal for confirmation
                    if (isWriteData(data.data) && data.data.preview) {
                        writeOp.setPreview?.(data.data.preview);
                    }
                    return {
                        type: 'write',
                        data: data.data,
                        pipeline,
                        intent
                    };
                }

                case 'Ddl': {
                    // DDL operation - show impact analysis for confirmation
                    if (isDdlData(data.data) && data.data.preview) {
                        ddlOp.setPreview?.(data.data.preview);
                    }
                    return {
                        type: 'ddl',
                        data: data.data,
                        pipeline,
                        intent
                    };
                }

                case 'Reject': {
                    // REJECT operation - show rejection message
                    if (isRejectionData(data.data)) {
                        setRejectionMessage({
                            intent: intent.type,
                            message: data.message,
                            reasoning: data.data.reason,
                            language: data.data.language
                        });
                    }
                    return {
                        type: 'reject',
                        data: data.data,
                        pipeline,
                        intent
                    };
                }

                case 'Query':
                default: {
                    // Standard QUERY operation - display results
                    if (isQueryData(data.data)) {
                        setQueryResponse({
                            success: data.success,
                            answer: data.data.answer,
                            sqlGenerated: data.sqlGenerated,
                            queryResult: data.data.queryResult,
                            queryExplanation: data.data.queryExplanation,
                            suggestedQueries: data.data.suggestedQueries,
                            contextEntities: data.data.contextEntities,
                            primaryEntity: data.data.primaryEntity,
                            pronounsResolved: data.data.pronounsResolved,
                            execution: data.execution
                        });
                    }
                    return {
                        type: 'query',
                        data: data.data,
                        pipeline,
                        intent
                    };
                }
            }

        } catch (err) {
            const errorMessage = err.response?.data?.error
                || err.response?.data?.message
                || err.message
                || 'Failed to send message';

            // ✅ P1: Handle SCHEMA_NOT_LOADED error with actionable info
            if (err.response?.data?.error === 'SCHEMA_NOT_LOADED') {
                const schemaError = {
                    type: 'SCHEMA_NOT_LOADED',
                    message: err.response.data.message,
                    action: err.response.data.action,
                    connectionId: err.response.data.connectionId,
                    suggestion: err.response.data.suggestion
                };
                setError(schemaError);
                console.warn('[useIntentBasedChat] Schema not loaded:', schemaError);

                return {
                    type: 'error',
                    error: schemaError,
                    details: err.response?.data
                };
            }

            setError(errorMessage);
            console.error('[useIntentBasedChat] Error:', err);

            return {
                type: 'error',
                error: errorMessage,
                details: err.response?.data
            };
        } finally {
            setLoading(false);
        }
    }, [connectionId, conversationId, writeOp, ddlOp]);

    /**
     * Execute WRITE operation after user confirmation
     * @param {string} question - Original question
     * @param {Object} preview - Preview data from generatePreview
     * @returns {Promise<Object>} Execution result
     */
    const executeWrite = useCallback(async (question, preview) => {
        const result = await writeOp.execute(question, connectionId, conversationId, preview);

        if (result && result.success) {
            // Update query response with success message
            setQueryResponse({
                success: true,
                answer: `✓ Successfully ${result.operationType?.toLowerCase() || 'modified'} ${result.actualAffectedRows || 0} row(s)`,
                sqlGenerated: result.sqlExecuted,
                queryResult: {
                    success: true,
                    rowsAffected: result.actualAffectedRows
                },
                suggestedQueries: result.suggestions || [],
                execution: {
                    duration: result.executionTime,
                    processingSteps: result.processingSteps || []
                }
            });
            setCurrentPipeline(PIPELINE_TYPES.QUERY);
        }

        return result;
    }, [connectionId, conversationId, writeOp]);

    /**
     * Execute DDL operation after user confirmation
     * @param {string} question - Original question
     * @param {Object} preview - Preview data from generatePreview
     * @returns {Promise<Object>} Execution result
     */
    const executeDDL = useCallback(async (question, preview) => {
        const result = await ddlOp.execute(question, connectionId, conversationId, preview);

        if (result && result.success) {
            // Update query response with success message
            setQueryResponse({
                success: true,
                answer: `✓ Successfully executed ${result.operationType || 'DDL operation'} on ${result.targetObject || 'database'}`,
                sqlGenerated: result.ddlExecuted,
                queryResult: {
                    success: true,
                    message: 'DDL operation completed successfully'
                },
                schemaReloaded: result.schemaCacheReloaded,
                execution: {
                    duration: result.executionTime,
                    processingSteps: result.processingSteps || []
                }
            });
            setCurrentPipeline(PIPELINE_TYPES.QUERY);
        }

        return result;
    }, [connectionId, conversationId, ddlOp]);

    /**
     * Reset all state
     */
    const reset = useCallback(() => {
        setQueryResponse(null);
        setForbiddenResult(null);
        setRejectionMessage(null);
        setError(null);
        setLoading(false);
        setCurrentPipeline(null);
        writeOp.reset();
        ddlOp.reset();
    }, [writeOp, ddlOp]);

    return {
        // State
        queryResponse,
        forbiddenResult,
        rejectionMessage,
        writePreview: writeOp.preview,
        ddlPreview: ddlOp.preview,
        currentPipeline,
        intentConfidence, // ✅ NEW: Intent classification confidence
        loading: loading || writeOp.loading || ddlOp.loading,
        error: error || writeOp.error || ddlOp.error,

        // Actions
        send,
        executeWrite,
        executeDDL,
        reset,

        // Sub-operation states (for advanced usage)
        writeOperation: {
            preview: writeOp.preview,
            result: writeOp.result,
            loading: writeOp.loading,
            error: writeOp.error
        },
        ddlOperation: {
            preview: ddlOp.preview,
            result: ddlOp.result,
            loading: ddlOp.loading,
            error: ddlOp.error
        }
    };
};

export default useIntentBasedChat;
