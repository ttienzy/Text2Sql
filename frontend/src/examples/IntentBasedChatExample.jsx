import React from 'react';
import { Layout, Alert } from 'antd';
import ChatInput from '../components/chat/ChatInput';
import MessageBubble from '../components/chat/MessageBubble';
import WriteConfirmationModal from '../components/write/WriteConfirmationModal';
import DDLImpactCard from '../components/ddl/DDLImpactCard';
import ForbiddenAlert from '../components/forbidden/ForbiddenAlert';
import useIntentBasedChat from '../hooks/useIntentBasedChat';

const { Content } = Layout;

/**
 * Example integration of intent-based chat with all pipelines
 * 
 * This component demonstrates how to integrate:
 * - QUERY pipeline (existing)
 * - WRITE pipeline (new) with confirmation modal
 * - DDL pipeline (new) with impact analysis
 * - FORBIDDEN pipeline (new) with safe alternatives
 */
const IntentBasedChatExample = ({ connectionId, conversationId }) => {
    const [messages, setMessages] = React.useState([]);
    const [currentQuestion, setCurrentQuestion] = React.useState('');

    const {
        queryResponse,
        forbiddenResult,
        writePreview,
        ddlPreview,
        loading,
        error,
        send,
        executeWrite,
        executeDDL,
        reset
    } = useIntentBasedChat(connectionId, conversationId);

    // Handle sending message
    const handleSend = async (question) => {
        setCurrentQuestion(question);

        // Add user message to chat
        setMessages(prev => [...prev, {
            role: 'user',
            content: question,
            timestamp: new Date()
        }]);

        // Send to backend
        const result = await send(question);

        // Handle different response types
        if (result.type === 'query' && result.data) {
            // QUERY response - add to messages
            setMessages(prev => [...prev, {
                role: 'assistant',
                content: result.data.answer,
                sqlGenerated: result.data.sqlGenerated,
                queryResult: result.data.queryResult,
                timestamp: new Date()
            }]);
        }

        // For WRITE, DDL, FORBIDDEN - modals will handle display
    };

    // Handle WRITE confirmation
    const handleWriteConfirm = async () => {
        const result = await executeWrite(currentQuestion, writePreview);

        if (result) {
            // Add success message to chat
            setMessages(prev => [...prev, {
                role: 'assistant',
                content: `✅ Successfully ${result.operationType.toLowerCase()}ed ${result.actualAffectedRows} row(s)`,
                sqlGenerated: result.sqlExecuted,
                timestamp: new Date()
            }]);
        }
    };

    // Handle DDL confirmation
    const handleDDLConfirm = async () => {
        const result = await executeDDL(currentQuestion, ddlPreview);

        if (result) {
            // Add success message to chat
            setMessages(prev => [...prev, {
                role: 'assistant',
                content: `✅ Successfully executed ${result.operationType} on ${result.targetObject}`,
                sqlGenerated: result.ddlExecuted,
                timestamp: new Date()
            }]);
        }
    };

    // Handle FORBIDDEN close
    const handleForbiddenClose = () => {
        // Add rejection message to chat
        if (forbiddenResult) {
            setMessages(prev => [...prev, {
                role: 'assistant',
                content: `⛔ ${forbiddenResult.rejectionReason}`,
                isForbidden: true,
                timestamp: new Date()
            }]);
        }
        reset();
    };

    return (
        <Layout style={{ maxWidth: 1200, margin: '0 auto', padding: '24px' }}>
            <Content style={{ height: '80vh', display: 'flex', flexDirection: 'column' }}>
                {/* Messages Area */}
                <div style={{ flex: 1, overflowY: 'auto', marginBottom: 16 }}>
                    {messages.map((message, index) => (
                        <MessageBubble
                            key={index}
                            message={message}
                            isUser={message.role === 'user'}
                        />
                    ))}

                    {error && (
                        <Alert
                            message="Error"
                            description={error}
                            type="error"
                            closable
                            onClose={reset}
                            style={{ marginBottom: 16 }}
                        />
                    )}
                </div>

                {/* Chat Input */}
                <ChatInput
                    onSend={handleSend}
                    disabled={loading}
                    placeholder="Ask a question or request a database operation..."
                />
            </Content>

            {/* WRITE Confirmation Modal */}
            <WriteConfirmationModal
                open={!!writePreview}
                preview={writePreview}
                onConfirm={handleWriteConfirm}
                onCancel={reset}
                loading={loading}
            />

            {/* DDL Impact Card */}
            <DDLImpactCard
                open={!!ddlPreview}
                preview={ddlPreview}
                onConfirm={handleDDLConfirm}
                onCancel={reset}
                loading={loading}
            />

            {/* FORBIDDEN Alert */}
            <ForbiddenAlert
                open={!!forbiddenResult}
                result={forbiddenResult}
                onClose={handleForbiddenClose}
            />
        </Layout>
    );
};

export default IntentBasedChatExample;
