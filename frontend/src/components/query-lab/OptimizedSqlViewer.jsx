import { Button, Space, Empty, Spin, message } from 'antd';
import { CopyOutlined, SendOutlined, CloseOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import Editor from '@monaco-editor/react';

const OptimizedSqlViewer = ({ result, loading, onClear }) => {
    const navigate = useNavigate();
    const detectedIssues = result?.detectedIssues || [];
    const hasBlockingIssues = result?.severity?.toLowerCase() === 'critical' ||
        detectedIssues.some((issue) => {
            const level = issue?.severity?.toLowerCase();
            return level === 'critical' || level === 'error';
        });
    const hasDetectedIssues = detectedIssues.length > 0;

    // Debug logging
    console.log('[OptimizedSqlViewer] result:', result);
    console.log('[OptimizedSqlViewer] optimizedSql:', result?.optimizedSql);

    const handleCopy = () => {
        if (result?.optimizedSql) {
            navigator.clipboard.writeText(result.optimizedSql);
            message.success('SQL copied to clipboard');
        }
    };

    const handleApplyToChat = () => {
        if (result?.optimizedSql) {
            navigate('/chat', {
                state: {
                    contextMessage: `I have this optimized SQL query:\n\n${result.optimizedSql}\n\nCan you help me understand or modify it?`,
                    contextType: 'query-lab',
                },
            });
        }
    };

    if (loading) {
        return (
            <div style={{
                height: '100%',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexDirection: 'column'
            }}>
                <Spin size="large" />
                <div style={{ marginTop: 16, fontSize: 14, color: '#999' }}>
                    Analyzing query...
                </div>
                <div style={{ marginTop: 8, fontSize: 12, color: '#bbb' }}>
                    This may take a few seconds
                </div>
            </div>
        );
    }

    if (!result) {
        return (
            <div style={{
                height: '100%',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center'
            }}>
                <Empty
                    description="No optimization result yet"
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                />
            </div>
        );
    }

    return (
        <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
            {/* Editor */}
            <div style={{ flex: 1, overflow: 'auto' }}>
                <Editor
                    height="100%"
                    defaultLanguage="sql"
                    value={result.optimizedSql || result.originalSql || '-- No SQL available'}
                    theme="vs-light"
                    options={{
                        readOnly: true,
                        minimap: { enabled: false },
                        fontSize: 14,
                        lineNumbers: 'on',
                        scrollBeyondLastLine: false,
                        automaticLayout: true,
                        tabSize: 2,
                        wordWrap: 'on',
                    }}
                />
            </div>

            {/* Action Buttons */}
            <div style={{
                padding: 16,
                borderTop: '1px solid #f0f0f0',
                background: '#fafafa',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center'
            }}>
                <div style={{ fontSize: 12, color: '#999' }}>
                    {hasBlockingIssues ? (
                        <span style={{ color: '#ff4d4f' }}>Query has blocking semantic errors</span>
                    ) : result.isChanged ? (
                        <span style={{ color: '#52c41a' }}>Query optimized</span>
                    ) : hasDetectedIssues ? (
                        <span style={{ color: '#faad14' }}>No rewrite applied. Review detected issues.</span>
                    ) : (
                        <span>Query is already optimal</span>
                    )}
                </div>
                <Space>
                    <Button
                        icon={<CloseOutlined />}
                        onClick={onClear}
                    >
                        Clear
                    </Button>
                    <Button
                        icon={<CopyOutlined />}
                        onClick={handleCopy}
                    >
                        Copy SQL
                    </Button>
                    <Button
                        type="primary"
                        icon={<SendOutlined />}
                        onClick={handleApplyToChat}
                        disabled={hasBlockingIssues}
                    >
                        Apply to Chat
                    </Button>
                </Space>
            </div>
        </div>
    );
};

export default OptimizedSqlViewer;

