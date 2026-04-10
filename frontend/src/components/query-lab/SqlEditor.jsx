import { useRef, useEffect } from 'react';
import { Button, Space } from 'antd';
import { ThunderboltOutlined, ClearOutlined } from '@ant-design/icons';
import Editor from '@monaco-editor/react';

const SqlEditor = ({ value, onChange, onAnalyze, loading }) => {
    const editorRef = useRef(null);

    const handleEditorDidMount = (editor) => {
        editorRef.current = editor;

        // Add keyboard shortcut: Ctrl+Enter to analyze
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => {
            if (!loading) {
                onAnalyze();
            }
        });
    };

    const handleClear = () => {
        onChange('');
        editorRef.current?.focus();
    };

    return (
        <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
            {/* Editor */}
            <div style={{ flex: 1, overflow: 'auto' }}>
                <Editor
                    height="100%"
                    defaultLanguage="sql"
                    value={value}
                    onChange={onChange}
                    onMount={handleEditorDidMount}
                    theme="vs-light"
                    options={{
                        minimap: { enabled: false },
                        fontSize: 14,
                        lineNumbers: 'on',
                        scrollBeyondLastLine: false,
                        automaticLayout: true,
                        tabSize: 2,
                        wordWrap: 'on',
                        formatOnPaste: true,
                        formatOnType: true,
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
                    Press <kbd>Ctrl+Enter</kbd> to analyze
                </div>
                <Space>
                    <Button
                        icon={<ClearOutlined />}
                        onClick={handleClear}
                        disabled={!value || loading}
                    >
                        Clear
                    </Button>
                    <Button
                        type="primary"
                        icon={<ThunderboltOutlined />}
                        onClick={onAnalyze}
                        loading={loading}
                        disabled={!value.trim()}
                    >
                        Analyze & Optimize
                    </Button>
                </Space>
            </div>
        </div>
    );
};

export default SqlEditor;
