import { useState, useEffect, useRef, useCallback } from 'react';
import {
  Typography,
  Badge,
  Button,
  Tooltip,
  message,
  Space,
} from 'antd';
import {
  RobotOutlined,
  ArrowDownOutlined,
  EditOutlined,
  LoadingOutlined,
} from '@ant-design/icons';
import { useLocation, useNavigate } from 'react-router-dom';
import { MessageBubble, ChatInput, MessageSkeleton, ConversationStatus } from '../chat';
import ThinkingIndicator from '../chat/ThinkingIndicator';
import ConversationLimitBanner from '../chat/ConversationLimitBanner';
import { ResponsiveChatMessageSkeleton } from '../common';
import WriteConfirmationModal from '../write/WriteConfirmationModal';
import ForbiddenAlert from '../forbidden/ForbiddenAlert';
import { useDelayedLoading } from '../../hooks/useDelayedLoading';
import { useStreamingQuery } from '../../hooks/useStreamingQuery';
import useConversationStore from '../../store/conversationStore';
import useConnectionStore from '../../store/connectionStore';
import { useMessagesQuery } from '../../api/messages';
import { useUpdateConversationMutation } from '../../api/conversations';
import { useProcessMessageMutation, confirmExecution, cancelExecution } from '../../api/agent';
import { useRefreshSchemaMutation } from '../../api/connections';
import { useQueryClient } from '@tanstack/react-query';
import { conversationKeys } from '../../api/conversations/queries';
import { useTablesQuery } from '../../api/dbExplorer';

const { Title, Text } = Typography;

/**
 * ChatArea - Main chat area component with messages display and input
 * Features: auto-scroll, optimistic updates, auto-title generation, 50-message context limit
 */
const ChatArea = ({ onSendMessage, isSending: externalIsSending, onNewConversation }) => {

  const [showScrollButton, setShowScrollButton] = useState(false);
  const [isEditingTitle, setIsEditingTitle] = useState(false);
  const [editedTitle, setEditedTitle] = useState('');
  const [currentQuestion, setCurrentQuestion] = useState(''); // Track current question for progress
  const [contextMessage, setContextMessage] = useState(''); // Context from DB Explorer
  const messagesEndRef = useRef(null);
  const messagesContainerRef = useRef(null);
  const isFirstMessageRef = useRef(false);

  // WRITE/DDL operation modal state
  const [showWriteModal, setShowWriteModal] = useState(false);
  const [writePreview, setWritePreview] = useState(null);
  const [pendingQuestion, setPendingQuestion] = useState('');
  const [isExecutingWrite, setIsExecutingWrite] = useState(false);

  // FORBIDDEN operation alert state
  const [showForbiddenAlert, setShowForbiddenAlert] = useState(false);
  const [forbiddenResult, setForbiddenResult] = useState(null);

  // ✅ SSE Streaming hook — primary query method
  const {
    stages: sseStages,
    currentStage: sseCurrentStage,
    progress: sseProgress,
    result: sseResult,
    error: sseError,
    isStreaming: sseIsStreaming,
    generatedSql: sseGeneratedSql,
    pendingConfirmation: ssePendingConfirmation,
    startStream,
    reset: resetStream,
  } = useStreamingQuery();

  // ✅ Generate unique IDs using useRef for lint compliance
  const messageIdRef = useRef(0);
  const getUniqueId = (prefix) => {
    messageIdRef.current += 1;
    return `${prefix}-${messageIdRef.current}-${Date.now()}`;
  };

  const location = useLocation();
  const navigate = useNavigate();
  const { currentConversation, messages, setMessages, updateConversationInList } = useConversationStore();
  const { activeConnection } = useConnectionStore();
  const queryClient = useQueryClient();
  const currentConversationRef = useRef(currentConversation);
  const currentQuestionRef = useRef(currentQuestion);

  const updateMessages = useCallback((updater) => {
    setMessages(updater);
  }, [setMessages]);

  useEffect(() => {
    currentConversationRef.current = currentConversation;
  }, [currentConversation]);

  useEffect(() => {
    currentQuestionRef.current = currentQuestion;
  }, [currentQuestion]);

  // React Query - fetch messages
  const {
    data: messagesData,
    isLoading: isLoadingMessages,
  } = useMessagesQuery(currentConversation?.id, {
    enabled: !!currentConversation?.id,
  });

  // Fetch table names for link detection
  const {
    data: tablesData,
  } = useTablesQuery(activeConnection?.id, {}, {
    enabled: !!activeConnection?.id,
  });

  // Extract table names from tables data
  const tableNames = tablesData?.tables?.map(t => t.tableName) || [];

  // Derive limit state from the query result
  const isLimitReached = messagesData?.limitReached ?? false;
  const messageTotalCount = messagesData?.totalCount ?? 0;
  const messageLimit = messagesData?.messageLimit ?? 50;

  // Use delayed loading to prevent skeleton flashing
  const showMessagesSkeleton = useDelayedLoading(isLoadingMessages && messages.length === 0, 300);

  // React Query - process message mutation with optimistic updates
  // Using v1 API (stable version)
  const processMessageMutation = useProcessMessageMutation({
    onMutate: async (variables) => {
      // Store current question for progress display
      setCurrentQuestion(variables.question);

      // Optimistically add user message
      const userMessage = {
        id: getUniqueId('temp-user'),
        conversationId: currentConversation?.id,
        role: 'user',
        content: variables.question,
        createdAt: new Date().toISOString(),
        isOptimistic: true,
      };

      updateMessages((prev) => [...prev, userMessage]);

      return { userMessage };
    },
    onSuccess: (unifiedResponse, variables) => {
      // Clear current question
      setCurrentQuestion('');

      // Remove optimistic messages and add real ones
      // Create user message
      const userMessage = {
        id: getUniqueId('user'),
        conversationId: currentConversationRef.current?.id,
        role: 'user',
        content: variables.question,
        createdAt: new Date().toISOString(),
      };

      // Extract data from UnifiedPipelineResponse
      let answer = unifiedResponse.message;
      let sqlQuery = unifiedResponse.sqlGenerated;
      let results = [];
      let rowCount = 0;
      let processingSteps = unifiedResponse.execution?.processingSteps || [];
      let suggestedQueries = [];
      let queryExplanation = null;

      // Extract pipeline-specific data
      if (unifiedResponse.data) {
        if (unifiedResponse.pipeline === 'Query' && unifiedResponse.data.answer) {
          answer = unifiedResponse.data.answer;
          results = unifiedResponse.data.queryResult?.rows || [];
          rowCount = unifiedResponse.data.queryResult?.rowCount || 0;
          suggestedQueries = unifiedResponse.data.suggestedQueries || [];
          queryExplanation = unifiedResponse.data.queryExplanation;
        }
      }

      // Create assistant message with rich content
      const assistantMessage = {
        id: getUniqueId('assistant'),
        conversationId: currentConversationRef.current?.id,
        role: 'assistant',
        content: answer,
        sqlQuery: sqlQuery,
        results: results,
        rowCount: rowCount,
        processingSteps: processingSteps,
        suggestedQueries: suggestedQueries,
        correctionHistory: [],
        wasCorrected: unifiedResponse.execution?.wasCorrected || false,
        queryExplanation: queryExplanation,
        createdAt: new Date().toISOString(),
        success: unifiedResponse.success,
        errorMessage: unifiedResponse.error?.message,
        // ✅ Include unified response fields
        pipeline: unifiedResponse.pipeline,
        intent: unifiedResponse.intent,
        requiresConfirmation: unifiedResponse.requiresConfirmation,
      };

      updateMessages((prev) => {
        const filteredMessages = prev.filter((message) => !message.isOptimistic && !message.isPending);
        return [...filteredMessages, userMessage, assistantMessage];
      });

      // ✅ FIX: Dispatch approval event when requiresConfirmation=true (for NotificationBell)
      if (unifiedResponse.requiresConfirmation) {
        window.dispatchEvent(new CustomEvent('agent:approval-needed', {
          detail: {
            sessionId: unifiedResponse.sessionId,
            question: variables.question,
            sqlPreview: sqlQuery,
            clarificationType: unifiedResponse.intent === 'ddl' ? 'ddl_operation' : 'dml_confirmation',
          }
        }));
      }

      // Auto-generate title from first question if needed
      if (currentConversation?.title === 'New Conversation' && !isFirstMessageRef.current) {
        isFirstMessageRef.current = true;
        const firstWords = variables.question.split(' ').slice(0, 5).join(' ');
        const newTitle = firstWords + (variables.question.split(' ').length > 5 ? '...' : '');
        updateTitleMutation.mutate({
          id: currentConversation.id,
          title: newTitle
        });
      }

      // Invalidate the conversations list query to update the message count
      queryClient.invalidateQueries({ queryKey: conversationKeys.lists() });

      message.success('Query executed successfully');
    },
    onError: (error, variables) => {
      // Clear current question
      setCurrentQuestion('');

      // Remove optimistic messages on error
      updateMessages((prev) => prev.filter((message) => !message.isOptimistic && !message.isPending));

      // ✅ P1: Handle SCHEMA_NOT_LOADED error with actionable UI
      if (error.response?.data?.error === 'SCHEMA_NOT_LOADED') {
        const schemaError = error.response.data;
        message.error({
          content: (
            <div>
              <div style={{ marginBottom: 8 }}>{schemaError.message}</div>
              <div style={{ fontSize: '12px', color: '#666', marginBottom: 8 }}>{schemaError.suggestion}</div>
              <Button
                type="primary"
                size="small"
                loading={refreshSchemaMutation.isPending}
                onClick={() => {
                  if (activeConnection?.id) {
                    refreshSchemaMutation.mutate(activeConnection.id, {
                      onSuccess: () => {
                        message.success('Schema refreshed! Try your query again.');
                      }
                    });
                  } else {
                    navigate('/connections');
                  }
                }}
              >
                {refreshSchemaMutation.isPending ? 'Refreshing...' : 'Refresh Schema'}
              </Button>
            </div>
          ),
          duration: 10,
        });
        return;
      }

      // ✅ Handle INPUT_NOT_SUPPORTED error (image input)
      if (error.response?.data?.error === 'INPUT_NOT_SUPPORTED') {
        const inputError = error.response.data;
        message.error(inputError.message || 'Image input is not supported. Please ask a text-based question.');
        return;
      }

      const errorMsg = error.response?.data?.message || error.message || 'Failed to process message';

      // ✅ FIX: Build suggestedQueries from error context for ErrorRecovery UI
      const lastQuery = variables?.question || currentQuestionRef.current;
      const suggestions = lastQuery ? [
        `Show all tables in the database`,
        `${lastQuery} (simplified)`,
      ] : [];

      const errorAssistant = {
        id: getUniqueId('error'),
        conversationId: currentConversationRef.current?.id,
        role: 'assistant',
        content: errorMsg,
        success: false,
        errorMessage: errorMsg,
        originalQuestion: lastQuery,
        suggestedQueries: suggestions,
        createdAt: new Date().toISOString(),
      };
      updateMessages((prev) => {
        const cleanMessages = prev.filter((message) => !message.isOptimistic && !message.isPending);
        return [...cleanMessages, errorAssistant];
      });

      message.error(errorMsg);
    },
  });

  // Refresh schema mutation for SCHEMA_NOT_LOADED errors
  const refreshSchemaMutation = useRefreshSchemaMutation();

  // Update conversation title mutation
  const updateTitleMutation = useUpdateConversationMutation({
    onSuccess: (data) => {
      updateConversationInList(data.id, { title: data.title });
      setIsEditingTitle(false);
    },
    onError: () => {
      message.error('Failed to update title');
      setIsEditingTitle(false);
    },
  });

  // Handle context from DB Explorer navigation
  useEffect(() => {
    if (location.state?.contextMessage) {
      const { contextMessage: msg, contextTable, contextType } = location.state;

      // Set the context message to populate the input
      setContextMessage(msg);

      // Show info message about the context
      const contextTypeLabel = contextType === 'query' ? 'Query' :
        contextType === 'relationships' ? 'Relationships' :
          contextType === 'quality' ? 'Quality Check' : 'Context';
      message.info(`${contextTypeLabel}: ${contextTable}`, 3);

      // Clear the location state to prevent re-triggering
      navigate(location.pathname, { replace: true, state: {} });
    }
  }, [location.state, location.pathname, navigate]);

  // Sync messages from query to store with error handling
  useEffect(() => {
    // messagesData is now { messages, totalCount, limitReached, messageLimit }
    const newMessages = messagesData?.messages;
    if (newMessages && Array.isArray(newMessages)) {
      try {
        setMessages(newMessages);
      } catch (error) {
        console.error('❌ Error setting messages:', error);

        // Handle localStorage quota exceeded
        if (error.name === 'QuotaExceededError') {
          console.warn('⚠️ LocalStorage quota exceeded, clearing conversation data...');

          // Clear conversation store data
          const { clearStorageData } = useConversationStore.getState();
          clearStorageData();

          // Show user-friendly message
          message.warning('Storage limit reached. Conversation history has been cleared to continue.');

          // Retry setting messages with cleared storage
          try {
            setMessages(newMessages);
          } catch (retryError) {
            console.error('❌ Failed to set messages after clearing storage:', retryError);
            message.error('Unable to load messages. Please refresh the page.');
          }
        } else {
          message.error('Failed to load conversation messages');
        }
      }
    }
  }, [messagesData, setMessages]);

  // Handle scroll to detect if we should show scroll button
  const handleScroll = useCallback(() => {
    if (!messagesContainerRef.current) return;

    const { scrollTop, scrollHeight, clientHeight } = messagesContainerRef.current;
    const isNearBottom = scrollHeight - scrollTop - clientHeight < 100;
    setShowScrollButton(!isNearBottom);
  }, []);

  // Scroll to bottom function
  const scrollToBottom = useCallback(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, []);

  // Auto-scroll when messages change
  useEffect(() => {
    if (messages.length > 0) {
      // Small delay to allow rendering
      const timeoutId = setTimeout(scrollToBottom, 100);
      return () => clearTimeout(timeoutId);
    }
  }, [messages, scrollToBottom]);

  // Handle sending a message — uses SSE streaming as primary, v1 mutation as fallback
  const handleSend = async (content) => {
    if (!currentConversation?.id || !activeConnection?.id) {
      message.warning('Please select a connection first');
      return;
    }

    try {
      // Clear context message after sending
      setContextMessage('');
      setCurrentQuestion(content);

      // Optimistically add user message
      const userMessage = {
        id: getUniqueId('temp-user'),
        conversationId: currentConversation?.id,
        role: 'user',
        content: content,
        createdAt: new Date().toISOString(),
        isOptimistic: true,
      };
      updateMessages((prev) => [...prev, userMessage]);

      // ✅ SSE STREAMING: Fire the real-time stream
      // Result handling is done via useEffect watchers below
      await startStream(
        content,
        currentConversation.id,
        activeConnection.id
      );

      // Call external callback if provided
      if (onSendMessage) {
        onSendMessage(content);
      }
    } catch (error) {
      console.error('Failed to send message:', error);
      // Fallback to v1 mutation
      try {
        await processMessageMutation.mutateAsync({
          connectionId: activeConnection.id,
          question: content,
          conversationId: currentConversation.id,
        });
      } catch (fallbackErr) {
        console.error('Fallback also failed:', fallbackErr);
      }
    }
  };

  // Handle title edit
  const handleTitleEdit = () => {
    setEditedTitle(currentConversation?.title || '');
    setIsEditingTitle(true);
  };

  const handleTitleSave = () => {
    if (editedTitle.trim() && editedTitle !== currentConversation?.title) {
      updateTitleMutation.mutate({
        id: currentConversation.id,
        title: editedTitle.trim()
      });
    } else {
      setIsEditingTitle(false);
    }
  };

  const handleTitleKeyDown = (e) => {
    if (e.key === 'Enter') {
      handleTitleSave();
    } else if (e.key === 'Escape') {
      setIsEditingTitle(false);
    }
  };

  // ✅ Watch for SSE pending confirmations to trigger the confirm modal
  useEffect(() => {
    if (ssePendingConfirmation && ssePendingConfirmation.confirmId) {
      setWritePreview(ssePendingConfirmation);
      setPendingQuestion(currentQuestionRef.current);
      setShowWriteModal(true);
    }
  }, [ssePendingConfirmation]);

  // Handle WRITE operation confirmation
  const handleConfirmWrite = async () => {
    if (!writePreview || !writePreview.confirmId) return;

    try {
      setIsExecutingWrite(true);

      // Tell backend to proceed
      await confirmExecution(writePreview.confirmId);

      // 💡 Important: we don't push success message here manually.
      // SSE connection is still open and will push 'executing' -> 'result' 
      // automatically when backend resumes polling.
      
      setShowWriteModal(false);
      setWritePreview(null);
      setPendingQuestion('');
      
    } catch (error) {
      console.error('Failed to confirm WRITE operation:', error);
      const errorMsg = error.response?.data?.message || error.message || 'Failed to confirm operation';
      message.error(errorMsg);
    } finally {
      setIsExecutingWrite(false);
    }
  };

  // Handle WRITE operation cancellation
  const handleCancelWrite = async () => {
    if (writePreview && writePreview.confirmId) {
       try {
          await cancelExecution(writePreview.confirmId, "Cancelled by user via UI");
       } catch (error) {
          console.error("Failed to send cancellation signal", error);
       }
    }
    
    setShowWriteModal(false);
    setWritePreview(null);
    setPendingQuestion('');
    // Current question will be cleared by the SSE result or error handler
    message.info('Operation cancelled');
  };

  // Handle FORBIDDEN operation alert close
  const handleForbiddenClose = () => {
    setShowForbiddenAlert(false);

    // Add rejection message to chat
    if (forbiddenResult) {
      const rejectionMessage = {
        id: getUniqueId('assistant'),
        conversationId: currentConversation?.id,
        role: 'assistant',
        content: `⛔ ${forbiddenResult.rejectionReason || 'This operation is not allowed'}`,
        success: false,
        isForbidden: true,
        createdAt: new Date().toISOString(),
      };

      updateMessages((prev) => {
        const currentMessages = prev.filter((message) => !message.isPending);
        return [...currentMessages, rejectionMessage];
      });
    }

    setForbiddenResult(null);
    setCurrentQuestion('');
  };

  // ✅ SSE RESULT WATCHER — convert SSE result into assistant message
  useEffect(() => {
    if (!sseResult) return;

    const pendingQuestion = currentQuestionRef.current;
    const activeConversation = currentConversationRef.current;

    // Check pipeline type to route to appropriate handler
    const pipelineType = sseResult.pipeline || sseResult.Pipeline;

    // FORBIDDEN operation - show alert
    if (pipelineType === 'Forbidden') {
      const result = sseResult.data?.result;
      if (result && result.isBlocked) {
        setForbiddenResult(result);
        setShowForbiddenAlert(true);

        // Add user message to chat
        const userMessage = {
          id: getUniqueId('user'),
          conversationId: activeConversation?.id,
          role: 'user',
          content: pendingQuestion,
          createdAt: new Date().toISOString(),
        };
        updateMessages((prev) => {
          const filteredMessages = prev.filter((message) => !message.isOptimistic && !message.isPending);
          return [...filteredMessages, userMessage];
        });

        resetStream();
        return; // Don't add assistant message - alert will handle display
      }
    }

    // WRITE operation - this part is now mostly legacy handling since actual confirmation pop is driven by SSE pendingConfirmation
    // However, if the API directly returns a Write preview as a Final Result (e.g. timeout without confirm?), we can handle it
    if (pipelineType === 'Write' || pipelineType === 'Dml') {
      const preview = sseResult.data?.preview;
      if (preview && sseResult.requiresConfirmation) {
        setWritePreview(preview);
        setPendingQuestion(pendingQuestion);
        setShowWriteModal(true);

        // Add user message to chat
        const userMessage = {
          id: getUniqueId('user'),
          conversationId: activeConversation?.id,
          role: 'user',
          content: pendingQuestion,
          createdAt: new Date().toISOString(),
        };
        updateMessages((prev) => {
          const filteredMessages = prev.filter((message) => !message.isOptimistic && !message.isPending);
          return [...filteredMessages, userMessage];
        });

        resetStream();
        return; // Don't add assistant message yet - wait for confirmation
      }
    }

    // DDL operation - similar handling (TODO: implement DDL modal)
    if (pipelineType === 'Ddl') {
      // TODO: Add DDL modal handling
      console.log('DDL operation detected - modal not yet implemented');
    }

    // Default: QUERY operation or other types
    // Build user message from the current question
    const userMessage = {
      id: getUniqueId('user'),
      conversationId: activeConversation?.id,
      role: 'user',
      content: pendingQuestion,
      createdAt: new Date().toISOString(),
    };

    // ✅ FIX: Map UnifiedPipelineResponse fields correctly
    // Backend sends: Message, SqlGenerated, Data, Success, Suggestions, etc.
    const answer = sseResult.message || sseResult.Message || '';
    const sqlQuery = sseResult.sqlGenerated || sseResult.SqlGenerated || sseResult.sql || '';

    // Extract data from pipeline-specific Data object
    let results = [];
    let rowCount = 0;
    let processingSteps = [];
    let suggestedQueries = [];

    if (sseResult.data) {
      // QueryPipelineData has QueryResult.Rows
      const data = sseResult.data;
      if (data.queryResult?.rows) {
        results = data.queryResult.rows;
        rowCount = data.queryResult.rowCount || 0;
      }
      if (data.suggestedQueries) {
        suggestedQueries = Array.isArray(data.suggestedQueries) ? data.suggestedQueries : [];
      }
      if (data.queryExplanation) {
        processingSteps = [data.queryExplanation];
      }
      // UI specific data visualizations
      if (data.chartImageBase64) {
          sseResult.ChartImageBase64 = data.chartImageBase64;
          sseResult.ChartType = data.chartType;
      }
    }

    // Fallback to top-level fields if Data is not present
    if (!results.length && sseResult.dataArray) {
      results = sseResult.dataArray;
    }
    if (!suggestedQueries.length && sseResult.suggestions) {
      suggestedQueries = sseResult.suggestions;
    }
    if (!suggestedQueries.length && sseResult.Suggestions) {
      suggestedQueries = sseResult.Suggestions;
    }
    if (!processingSteps.length && sseResult.processingSteps) {
      processingSteps = sseResult.processingSteps;
    }
    if (!processingSteps.length && sseResult.Execution?.processingSteps) {
      processingSteps = sseResult.Execution.processingSteps;
    }

    // Build assistant message from SSE result
    const assistantMessage = {
      id: getUniqueId('assistant'),
      conversationId: activeConversation?.id,
      role: 'assistant',
      content: answer || sseResult.errorMessage || 'Query processed.',
      sqlQuery: sqlQuery,
      results: results,
      rowCount: rowCount || results.length,
      processingSteps: processingSteps,
      suggestedQueries: suggestedQueries,
      createdAt: new Date().toISOString(),
      success: sseResult.success || sseResult.Success,
      errorMessage: (sseResult.success || sseResult.Success) ? null : (sseResult.errorMessage || sseResult.Error?.message),
      correlationId: sseResult.correlationId,
      chartImageBase64: sseResult.ChartImageBase64 || sseResult.chartImageBase64,
      chartType: sseResult.ChartType || sseResult.chartType,
    };

    updateMessages((prev) => {
      const filteredMessages = prev.filter((message) => !message.isOptimistic && !message.isPending);
      return [...filteredMessages, userMessage, assistantMessage];
    });
    setCurrentQuestion('');

    // Dispatch approval event if needed (for non-WRITE operations)
    if (sseResult.requiresConfirmation && pipelineType !== 'Write') {
      window.dispatchEvent(new CustomEvent('agent:approval-needed', {
        detail: {
          sessionId: sseResult.correlationId,
          question: pendingQuestion,
          sqlPreview: sseResult.sql,
          clarificationType: 'dml_confirmation',
        }
      }));
    }

    // Auto-generate title 
    if (activeConversation?.title === 'New Conversation' && !isFirstMessageRef.current) {
      isFirstMessageRef.current = true;
      const firstWords = pendingQuestion.split(' ').slice(0, 5).join(' ');
      const newTitle = firstWords + (pendingQuestion.split(' ').length > 5 ? '...' : '');
      updateTitleMutation.mutate({ id: activeConversation.id, title: newTitle });
    }

    queryClient.invalidateQueries({ queryKey: conversationKeys.lists() });
    const isSuccess = sseResult.success || sseResult.Success;
    if (isSuccess) message.success('Query executed successfully');

    resetStream();
  }, [sseResult, queryClient, resetStream, updateMessages, updateTitleMutation]);

  // ✅ SSE ERROR WATCHER — convert SSE error into error message
  useEffect(() => {
    if (!sseError) return;
    const pendingQuestion = currentQuestionRef.current;
    const activeConversation = currentConversationRef.current;

    // ✅ P0: Enhanced error handling for SCHEMA_NOT_LOADED
    let errorMsg = sseError.message || sseError.Message || 'Streaming error occurred';
    let actionButton = null;

    if (sseError.code === 'SCHEMA_NOT_LOADED' || sseError.Code === 'SCHEMA_NOT_LOADED') {
      errorMsg = '⚠️ Database schema not loaded. Please test your connection first to load the schema.';
      actionButton = {
        label: 'Test Connection',
        action: () => {
          message.info('Please go to Connections page and test your connection');
        }
      };
    } else if (sseError.code === 'CONNECTION_NOT_FOUND' || sseError.Code === 'CONNECTION_NOT_FOUND') {
      errorMsg = '⚠️ Connection not found or access denied. Please select a valid connection.';
    } else if (sseError.code === 'UNAUTHORIZED' || sseError.Code === 'UNAUTHORIZED') {
      errorMsg = '⚠️ Authentication failed. Please log in again.';
    } else if (sseError.code === 'PROCESSING_ERROR' || sseError.Code === 'PROCESSING_ERROR') {
      errorMsg = `⚠️ ${sseError.message || 'An error occurred during processing'}`;
    } else if (sseError.code === 'INPUT_NOT_SUPPORTED' || sseError.Code === 'INPUT_NOT_SUPPORTED') {
      errorMsg = sseError.message || 'Image input is not supported. Please ask a text-based question about your database.';
    }

    const errorAssistant = {
      id: getUniqueId('error'),
      conversationId: activeConversation?.id,
      role: 'assistant',
      content: errorMsg,
      success: false,
      errorMessage: errorMsg,
      errorCode: sseError.code,
      actionButton: actionButton,
      originalQuestion: pendingQuestion,
      suggestedQueries: [
        'Show all tables in the database',
        `${pendingQuestion} (simplified)`,
      ],
      createdAt: new Date().toISOString(),
    };

    updateMessages((prev) => {
      const filteredMessages = prev.filter((message) => !message.isOptimistic && !message.isPending);
      return [...filteredMessages, errorAssistant];
    });
    setCurrentQuestion('');
    message.error(errorMsg);
    resetStream();
  }, [resetStream, sseError, updateMessages]);

  // Welcome screen when no conversation is selected
  if (!currentConversation) {
    return (
      <div style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        padding: 48,
        textAlign: 'center',
      }}>
        <RobotOutlined style={{ fontSize: 64, color: '#1890ff', marginBottom: 24 }} />
        <Title level={3} style={{ marginBottom: 8 }}>
          Welcome to TextToSQL Agent
        </Title>
        <Text type="secondary" style={{ fontSize: 16, maxWidth: 400 }}>
          {!activeConnection
            ? 'Please select a database connection to start chatting.'
            : 'Select a conversation or start a new one to begin querying your database.'}
        </Text>
      </div>
    );
  }

  // Check if sending (either internal, external, or processing)
  const isSending = externalIsSending || processMessageMutation.isPending || sseIsStreaming;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      {/* Conversation Header */}
      <div style={{
        padding: '12px 16px',
        borderBottom: '1px solid #f0f0f0',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        backgroundColor: '#fff',
      }}>
        <div style={{ flex: 1 }}>
          {isEditingTitle ? (
            <input
              value={editedTitle}
              onChange={(e) => setEditedTitle(e.target.value)}
              onBlur={handleTitleSave}
              onKeyDown={handleTitleKeyDown}
              autoFocus
              style={{
                fontSize: 16,
                fontWeight: 500,
                border: '1px solid #1890ff',
                borderRadius: 4,
                padding: '2px 8px',
                outline: 'none',
                width: '100%',
                maxWidth: 300,
              }}
            />
          ) : (
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <Title level={5} style={{ margin: 0, display: 'inline-block' }}>
                {currentConversation.title || 'Untitled Conversation'}
              </Title>
              <Tooltip title="Edit title">
                <Button
                  type="text"
                  size="small"
                  icon={<EditOutlined />}
                  onClick={handleTitleEdit}
                  style={{ fontSize: 12, color: '#8c8c8c' }}
                />
              </Tooltip>
            </div>
          )}
          <Text type="secondary" style={{ fontSize: 12 }}>
            {messages.length} {messages.length === 1 ? 'message' : 'messages'}
          </Text>
        </div>
        {activeConnection && (
          <Badge
            status="success"
            text={activeConnection.name}
          />
        )}
      </div>

      {/* Messages Container */}
      <div
        ref={messagesContainerRef}
        style={{
          flex: 1,
          overflow: 'auto',
          padding: '16px',
          backgroundColor: '#fafafa',
          position: 'relative',
        }}
        onScroll={handleScroll}
      >
        {showMessagesSkeleton ? (
          <ResponsiveChatMessageSkeleton />
        ) : isLoadingMessages ? (
          <div style={{ flex: 1 }} /> // Blank state during the first 300ms delay to prevent flashing
        ) : messages.length === 0 ? (
          <div style={{ textAlign: 'center', padding: 48 }}>
            <Text type="secondary">
              Start the conversation by asking a question about your database.
            </Text>
          </div>
        ) : (
          <div>
            {messages.map((messageItem, index) => (
              <MessageBubble
                key={messageItem.id || index}
                message={messageItem}
                onSuggestedQueryClick={handleSend}
                tableNames={tableNames}
              />
            ))}

            {/* ✅ SSE: Premium thinking indicator during streaming */}
            {sseIsStreaming && currentQuestion && (
              <ThinkingIndicator
                stages={sseStages}
                currentStage={sseCurrentStage}
                progress={sseProgress}
                isStreaming={sseIsStreaming}
                error={sseError}
                generatedSql={sseGeneratedSql}
              />
            )}


          </div>
        )}

        {/* Invisible element to scroll to */}
        <div ref={messagesEndRef} />
      </div>

      {/* Scroll to bottom button */}
      {showScrollButton && (
        <Tooltip title="Scroll to bottom">
          <Button
            type="primary"
            shape="circle"
            icon={<ArrowDownOutlined />}
            onClick={scrollToBottom}
            style={{
              position: 'absolute',
              bottom: 90,
              right: 24,
              boxShadow: '0 2px 8px rgba(0,0,0,0.15)',
            }}
          />
        </Tooltip>
      )}

      {/* Conversation Limit Banner */}
      <ConversationLimitBanner
        visible={isLimitReached}
        totalCount={messageTotalCount}
        messageLimit={messageLimit}
        onNewConversation={onNewConversation}
      />

      {/* Chat Input */}
      <ChatInput
        onSend={handleSend}
        isLoading={isSending || sseIsStreaming}
        disabled={!activeConnection || isLimitReached}
        placeholder={
          isLimitReached
            ? `Context limit reached (${messageTotalCount}/${messageLimit} messages). Please start a new conversation.`
            : activeConnection
              ? 'Ask a question about your database...'
              : 'Select a connection first'
        }
        initialValue={contextMessage}
      />

      {/* Conversation Status */}
      {currentConversation && messages.length > 0 && (
        <ConversationStatus
          conversationId={currentConversation.id}
          messageCount={messages.length}
          isConversationMode={true}
          lastMessageTime={messages[messages.length - 1]?.createdAt}
          contextMessagesCount={Math.min(messages.length, 6)}  // ✅ Show last 6 messages used as context
          compact={true}
        />
      )}

      {/* WRITE Confirmation Modal */}
      <WriteConfirmationModal
        open={showWriteModal}
        preview={writePreview}
        onConfirm={handleConfirmWrite}
        onCancel={handleCancelWrite}
        loading={isExecutingWrite}
      />

      {/* FORBIDDEN Alert */}
      <ForbiddenAlert
        open={showForbiddenAlert}
        result={forbiddenResult}
        onClose={handleForbiddenClose}
      />
    </div>
  );
};

export default ChatArea;
