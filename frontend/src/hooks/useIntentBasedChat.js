import { useState, useCallback } from 'react';
import axios from '../api/axios';
import { useWriteOperation } from '../api/write';
import { useDDLOperation } from '../api/ddl';
import { API_ENDPOINTS, PIPELINE_TYPES } from '../constants/api';

/**
 * Unified hook for intent-based chat with automatic pipeline routing
 * Handles QUERY, WRITE, DDL, and FORBIDDEN operations
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
        writeOp.reset();
        ddlOp.reset();

        try {
            // Send to main agent endpoint - backend will route based on intent
            const response = await axios.post(API_ENDPOINTS.AGENT.PROCESS, {
                question,
                connectionId,
                conversationId
            });

            const data = response.data;

            // Check if response indicates a specific pipeline
            const pipeline = data.pipeline || PIPELINE_TYPES.QUERY;
            setCurrentPipeline(pipeline);

            // Route based on pipeline type
            switch (pipeline) {
                case PIPELINE_TYPES.FORBIDDEN:
                    // FORBIDDEN operation - show alert with safe alternatives
                    setForbiddenResult(data.forbiddenResult || data);
                    return {
                        type: 'forbidden',
                        data: data.forbiddenResult || data,
                        pipeline
                    };

                case PIPELINE_TYPES.WRITE:
                    // WRITE operation - show preview modal for confirmation
                    const writePreview = data.writePreview || data.preview;
                    if (writePreview) {
                        // Store preview in write operation hook
                        writeOp.setPreview?.(writePreview);
                    }
                    return {
                        type: 'write',
                        data: writePreview,
                        pipeline
                    };

                case PIPELINE_TYPES.DDL:
                    // DDL operation - show impact analysis for confirmation
                    const ddlPreview = data.ddlPreview || data.preview;
                    if (ddlPreview) {
                        // Store preview in DDL operation hook
                        ddlOp.setPreview?.(ddlPreview);
                    }
                    return {
                        type: 'ddl',
                        data: ddlPreview,
                        pipeline
                    };

                case PIPELINE_TYPES.REJECT:
                    // REJECT operation - show rejection message
                    setRejectionMessage({
                        intent: data.intent,
                        message: data.message,
                        reasoning: data.reasoning,
                        language: data.language
                    });
                    return {
                        type: 'reject',
                        data: {
                            intent: data.intent,
                            message: data.message,
                            reasoning: data.reasoning,
                            language: data.language
                        },
                        pipeline
                    };

                case PIPELINE_TYPES.QUERY:
                default:
                    // Standard QUERY operation - display results
                    setQueryResponse(data);
                    return {
                        type: 'query',
                        data,
                        pipeline
                    };
            }

        } catch (err) {
            const errorMessage = err.response?.data?.error
                || err.response?.data?.message
                || err.message
                || 'Failed to send message';

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
                suggestedQueries: result.suggestions || []
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
                    message: result.executionMessage
                },
                schemaReloaded: result.schemaCacheReloaded
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
