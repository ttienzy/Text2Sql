import React, { useState, useEffect } from 'react';
import { Alert } from 'antd';
import useIntentBasedChat from '../../hooks/useIntentBasedChat';
import WriteConfirmationModal from '../write/WriteConfirmationModal';
import DDLImpactCard from '../ddl/DDLImpactCard';
import ForbiddenAlert from '../forbidden/ForbiddenAlert';

/**
 * Integrated chat interface with intent-based routing
 * Automatically shows appropriate modals based on operation type
 * 
 * Note: This component only manages modals. Parent component handles chat UI.
 * Use the exported useIntentBasedChat hook directly for more control.
 * 
 * @param {Object} props
 * @param {string} props.connectionId - Database connection ID
 * @param {string} props.conversationId - Conversation ID
 * @param {Function} props.onQueryResponse - Callback when query response received
 * @param {Function} props.onError - Callback when error occurs
 */
const IntentBasedChatInterface = ({
    connectionId,
    conversationId,
    onQueryResponse,
    onError
}) => {
    const {
        queryResponse,
        forbiddenResult,
        rejectionMessage,
        writePreview,
        ddlPreview,
        loading,
        error,
        executeWrite,
        executeDDL,
        reset
    } = useIntentBasedChat(connectionId, conversationId);

    // Modal states
    const [showWriteModal, setShowWriteModal] = useState(false);
    const [showDDLModal, setShowDDLModal] = useState(false);
    const [showForbiddenAlert, setShowForbiddenAlert] = useState(false);
    const [showRejectionAlert, setShowRejectionAlert] = useState(false);
    const [pendingQuestion, setPendingQuestion] = useState('');

    // Handle write preview
    useEffect(() => {
        if (writePreview) {
            setShowWriteModal(true);
        }
    }, [writePreview]);

    // Handle DDL preview
    useEffect(() => {
        if (ddlPreview) {
            setShowDDLModal(true);
        }
    }, [ddlPreview]);

    // Handle forbidden result
    useEffect(() => {
        if (forbiddenResult) {
            setShowForbiddenAlert(true);
        }
    }, [forbiddenResult]);

    // Handle rejection message
    useEffect(() => {
        if (rejectionMessage) {
            setShowRejectionAlert(true);
        }
    }, [rejectionMessage]);

    // Handle query response
    useEffect(() => {
        if (queryResponse && onQueryResponse) {
            onQueryResponse(queryResponse);
        }
    }, [queryResponse, onQueryResponse]);

    // Handle errors
    useEffect(() => {
        if (error && onError) {
            onError(error);
        }
    }, [error, onError]);

    /**
     * Confirm and execute WRITE operation
     */
    const handleConfirmWrite = async () => {
        if (!writePreview || !pendingQuestion) return;

        const result = await executeWrite(pendingQuestion, writePreview);
        setShowWriteModal(false);
        setPendingQuestion('');

        return result;
    };

    /**
     * Cancel WRITE operation
     */
    const handleCancelWrite = () => {
        setShowWriteModal(false);
        setPendingQuestion('');
    };

    /**
     * Confirm and execute DDL operation
     */
    const handleConfirmDDL = async () => {
        if (!ddlPreview || !pendingQuestion) return;

        const result = await executeDDL(pendingQuestion, ddlPreview);
        setShowDDLModal(false);
        setPendingQuestion('');

        return result;
    };

    /**
     * Cancel DDL operation
     */
    const handleCancelDDL = () => {
        setShowDDLModal(false);
        setPendingQuestion('');
    };

    /**
     * Close forbidden alert
     */
    const handleCloseForbidden = () => {
        setShowForbiddenAlert(false);
        setPendingQuestion('');
    };

    /**
     * Close rejection alert
     */
    const handleCloseRejection = () => {
        setShowRejectionAlert(false);
    };

    return (
        <div>
            {/* Error Display */}
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

            {/* WRITE Confirmation Modal */}
            <WriteConfirmationModal
                open={showWriteModal}
                preview={writePreview}
                onConfirm={handleConfirmWrite}
                onCancel={handleCancelWrite}
                loading={loading}
            />

            {/* DDL Impact Analysis Modal */}
            <DDLImpactCard
                open={showDDLModal}
                preview={ddlPreview}
                onConfirm={handleConfirmDDL}
                onCancel={handleCancelDDL}
                loading={loading}
            />

            {/* FORBIDDEN Alert */}
            <ForbiddenAlert
                open={showForbiddenAlert}
                result={forbiddenResult}
                onClose={handleCloseForbidden}
            />

            {/* REJECTION Alert */}
            {rejectionMessage && (
                <Alert
                    message={rejectionMessage.intent === 'OffTopic'
                        ? (rejectionMessage.language === 'vi' ? "Câu Hỏi Ngoài Phạm Vi" : "Off-Topic Question")
                        : (rejectionMessage.language === 'vi' ? "Không Thể Xử Lý" : "Cannot Process Request")}
                    description={rejectionMessage.message}
                    type="warning"
                    closable
                    onClose={handleCloseRejection}
                    showIcon
                    style={{
                        marginBottom: 16,
                        display: showRejectionAlert ? 'block' : 'none'
                    }}
                />
            )}
        </div>
    );
};

// Export hook for direct usage
export { useIntentBasedChat };

export default IntentBasedChatInterface;
