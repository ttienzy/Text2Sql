import { useState, useEffect, useRef, useCallback } from 'react';
import {
  Typography,
  Badge,
  Button,
  Tooltip,
  message,
} from 'antd';
import {
  RobotOutlined,
  ArrowDownOutlined,
  EditOutlined,
} from '@ant-design/icons';
import { useLocation, useNavigate } from 'react-router-dom';
import { MessageBubble, ChatInput, MessageSkeleton, ConversationStatus } from '../chat';
import SmartProcessingProgress from '../chat/SmartProcessingProgress';
import ConversationLimitBanner from '../chat/ConversationLimitBanner';
import { ResponsiveChatMessageSkeleton } from '../common';
import { useDelayedLoading } from '../../hooks/useDelayedLoading';
import useConversationStore from '../../store/conversationStore';
import useConnectionStore from '../../store/connectionStore';
import { useMessagesQuery } from '../../api/messages';
import { useUpdateConversationMutation } from '../../api/conversations';
import { useProcessMessageMutation } from '../../api/agent';
import { useQueryClient } from '@tanstack/react-query';
import { conversationKeys } from '../../api/conversations/queries';
import { useTablesQuery } from '../../api/dbExplorer';
// import { useProcessMessageV2Mutation } from '../../api/agent/v2';

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

  const location = useLocation();
  const navigate = useNavigate();
  const { currentConversation, messages, setMessages, updateConversationInList } = useConversationStore();
  const { activeConnection } = useConnectionStore();
  const queryClient = useQueryClient();

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
        id: `temp-user-${Date.now()}`,
        conversationId: currentConversation?.id,
        role: 'user',
        content: variables.question,
        createdAt: new Date().toISOString(),
        isOptimistic: true,
      };

      setMessages([...messages, userMessage]);

      return { userMessage };
    },
    onSuccess: (data, variables, context) => {
      // Clear current question
      setCurrentQuestion('');

      // Remove optimistic messages and add real ones
      const filteredMessages = messages.filter(m => !m.isOptimistic && !m.isPending);

      // Create user message
      const userMessage = {
        id: `user-${Date.now()}`,
        conversationId: currentConversation?.id,
        role: 'user',
        content: variables.question,
        createdAt: new Date().toISOString(),
      };

      // Create assistant message with rich content
      const assistantMessage = {
        id: `assistant-${Date.now()}`,
        conversationId: currentConversation?.id,
        role: 'assistant',
        content: data.answer || 'Query processed successfully',
        sqlQuery: data.sqlGenerated,
        results: data.queryResult?.rows || [],
        rowCount: data.queryResult?.rowCount || 0,
        processingSteps: data.processingSteps || [],
        suggestedQueries: data.suggestedQueries || [],
        correctionHistory: data.correctionHistory || [],
        wasCorrected: data.wasCorrected || false,
        queryExplanation: data.queryExplanation,
        createdAt: new Date().toISOString(),
        success: data.success,
        errorMessage: data.errorMessage,
      };

      setMessages([...filteredMessages, userMessage, assistantMessage]);

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
    onError: (error, variables, context) => {
      // Clear current question
      setCurrentQuestion('');

      // Remove optimistic messages on error
      const filteredMessages = messages.filter(m => !m.isOptimistic && !m.isPending);
      setMessages(filteredMessages);

      const errorMsg = error.response?.data?.message || error.message || 'Failed to process message';
      message.error(errorMsg);
    },
  });

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

  // Handle sending a message - uses production API
  const handleSend = async (content) => {
    if (!currentConversation?.id || !activeConnection?.id) {
      message.warning('Please select a connection first');
      return;
    }

    try {
      // Clear context message after sending
      setContextMessage('');

      // Use production API to process message (v1 for now)
      await processMessageMutation.mutateAsync({
        connectionId: activeConnection.id,
        question: content,
        conversationId: currentConversation.id,
      });

      // Call external callback if provided
      if (onSendMessage) {
        onSendMessage(content);
      }
    } catch (error) {
      console.error('Failed to send message:', error);
      // Error handling is done in the mutation onError callback
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
  const isSending = externalIsSending || processMessageMutation.isPending;

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

            {/* Show enhanced progress when processing */}
            {isSending && currentQuestion && (
              <SmartProcessingProgress
                question={currentQuestion}
                isVisible={true}
                connectionName={activeConnection?.name || 'Database'}
                mode="enhanced" // Can be 'enhanced', 'simple', or 'streaming'
                onStepComplete={(stepTitle, stepIndex) => {
                  console.log(`Completed step: ${stepTitle} (${stepIndex})`);
                }}
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
        isLoading={isSending}
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
    </div>
  );
};

export default ChatArea;
