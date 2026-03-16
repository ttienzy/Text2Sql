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
import { MessageBubble, ChatInput, MessageSkeleton } from '../chat';
import { ResponsiveChatMessageSkeleton } from '../common';
import { useDelayedLoading } from '../../hooks/useDelayedLoading';
import useConversationStore from '../../store/conversationStore';
import useConnectionStore from '../../store/connectionStore';
import { useMessagesQuery } from '../../api/messages';
import { useUpdateConversationMutation } from '../../api/conversations';
import { useProcessMessageMutation } from '../../api/agent';

const { Title, Text } = Typography;

/**
 * ChatArea - Main chat area component with messages display and input
 * Features: auto-scroll, optimistic updates, auto-title generation
 */
const ChatArea = ({ onSendMessage, isSending: externalIsSending }) => {

  const [showScrollButton, setShowScrollButton] = useState(false);
  const [isEditingTitle, setIsEditingTitle] = useState(false);
  const [editedTitle, setEditedTitle] = useState('');
  const messagesEndRef = useRef(null);
  const messagesContainerRef = useRef(null);
  const isFirstMessageRef = useRef(false);

  const { currentConversation, messages, setMessages, updateConversationInList } = useConversationStore();
  const { activeConnection } = useConnectionStore();

  // React Query - fetch messages
  const {
    data: messagesData,
    isLoading: isLoadingMessages,
  } = useMessagesQuery(currentConversation?.id, {
    enabled: !!currentConversation?.id,
  });

  // Use delayed loading to prevent skeleton flashing
  const showMessagesSkeleton = useDelayedLoading(isLoadingMessages && messages.length === 0, 300);

  // React Query - process message mutation with optimistic updates
  const processMessageMutation = useProcessMessageMutation({
    onMutate: async (variables) => {
      // Optimistically add user message
      const userMessage = {
        id: `temp-user-${Date.now()}`,
        conversationId: currentConversation?.id,
        role: 'user',
        content: variables.question,
        createdAt: new Date().toISOString(),
        isOptimistic: true,
      };

      // Add pending assistant message
      const assistantMessage = {
        id: `temp-assistant-${Date.now()}`,
        conversationId: currentConversation?.id,
        role: 'assistant',
        content: '',
        isPending: true,
        createdAt: new Date().toISOString(),
      };

      setMessages([...messages, userMessage, assistantMessage]);

      return { userMessage, assistantMessage };
    },
    onSuccess: (data, variables, context) => {
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

      message.success('Query executed successfully');
    },
    onError: (error, variables, context) => {
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

  // Sync messages from query to store
  useEffect(() => {
    if (messagesData && Array.isArray(messagesData)) {
      setMessages(messagesData);
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
      // Use production API to process message
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
              />
            ))}

            {/* Show skeleton when processing */}
            {isSending && (
              <MessageSkeleton isUser={false} />
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

      {/* Chat Input */}
      <ChatInput
        onSend={handleSend}
        isLoading={isSending}
        disabled={!activeConnection}
        placeholder={activeConnection
          ? "Ask a question about your database..."
          : "Select a connection first"
        }
      />
    </div>
  );
};

export default ChatArea;
