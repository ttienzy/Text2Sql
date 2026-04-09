import React from 'react';
import { Alert } from 'antd';
import { WarningOutlined } from '@ant-design/icons';
import ReactMarkdown from 'react-markdown';

const ForbiddenWarning = ({ message }) => {
    if (!message) return null;

    const decodeHtml = (str) => {
        const txt = document.createElement("textarea");
        txt.innerHTML = str;
        return txt.value;
    };

    const decodedMessage = decodeHtml(message);

    return (
        <Alert
            type="error"
            showIcon
            icon={<WarningOutlined />}
            message="Action Blocked"
            description={
                <div className="markdown-container" style={{ 
                    fontSize: 13, 
                    lineHeight: 1.6,
                    maxHeight: '400px',
                    overflowY: 'auto'
                }}>
                    <ReactMarkdown>{decodedMessage}</ReactMarkdown>
                </div>
            }
            style={{ marginBottom: 12 }}
        />
    );
};

export default ForbiddenWarning;
