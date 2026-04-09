import { useState } from 'react';
import { Modal, Radio, Space, Button, Alert, message } from 'antd';
import { DownloadOutlined, FileMarkdownOutlined, FileTextOutlined } from '@ant-design/icons';
import { useExportDocumentationMutation } from '../../api/dbExplorer/commands';

const ExportDocumentationModal = ({ visible, onClose, connectionId, databaseName }) => {
    const [exportFormat, setExportFormat] = useState('markdown');

    const exportMutation = useExportDocumentationMutation({
        onSuccess: ({ data, format }) => {
            // Create blob and download
            const blob = new Blob([data], { type: 'text/markdown' });
            const url = window.URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;

            const timestamp = new Date().toISOString().split('T')[0];
            const extension = format === 'markdown' ? 'md' : 'txt';
            link.download = `${databaseName || 'database'}_documentation_${timestamp}.${extension}`;

            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);
            window.URL.revokeObjectURL(url);

            message.success('Documentation exported successfully!');
            onClose();
        },
        onError: (error) => {
            message.error(`Export failed: ${error.response?.data?.error || error.message}`);
        },
    });

    const handleExport = () => {
        if (!connectionId) {
            message.error('No connection selected');
            return;
        }

        exportMutation.mutate({ connectionId, format: exportFormat });
    };

    return (
        <Modal
            title={
                <Space>
                    <DownloadOutlined />
                    <span>Export Database Documentation</span>
                </Space>
            }
            open={visible}
            onCancel={onClose}
            footer={[
                <Button key="cancel" onClick={onClose}>
                    Cancel
                </Button>,
                <Button
                    key="export"
                    type="primary"
                    icon={<DownloadOutlined />}
                    onClick={handleExport}
                    loading={exportMutation.isPending}
                >
                    Download Documentation
                </Button>,
            ]}
            width={600}
        >
            <div style={{ marginBottom: 16 }}>
                <Alert
                    message="Export database schema documentation"
                    description="Generate comprehensive documentation with AI insights, table details, and health analysis."
                    type="info"
                    showIcon
                    style={{ marginBottom: 16 }}
                />
            </div>

            <div style={{ marginBottom: 24 }}>
                <div style={{ marginBottom: 12, fontWeight: 500 }}>Select Export Format:</div>
                <Radio.Group
                    onChange={(e) => setExportFormat(e.target.value)}
                    value={exportFormat}
                    style={{ width: '100%' }}
                >
                    <Space direction="vertical" style={{ width: '100%' }}>
                        <Radio value="markdown" style={{ width: '100%' }}>
                            <div style={{ padding: '12px 0' }}>
                                <Space>
                                    <FileMarkdownOutlined style={{ fontSize: 20, color: '#1890ff' }} />
                                    <div>
                                        <div style={{ fontWeight: 500 }}>📄 Markdown (Full Documentation)</div>
                                        <div style={{ fontSize: 12, color: '#666', marginTop: 4 }}>
                                            Complete documentation with:
                                            <ul style={{ margin: '4px 0 0 0', paddingLeft: 20 }}>
                                                <li>Database overview and AI insights</li>
                                                <li>All tables with columns, types, and constraints</li>
                                                <li>Relationships and ER diagram description</li>
                                                <li>Health issues and recommendations</li>
                                                <li>Index analysis and suggestions</li>
                                            </ul>
                                        </div>
                                        <div style={{ fontSize: 11, color: '#999', marginTop: 4 }}>
                                            Best for: Comprehensive documentation, team sharing, archival
                                        </div>
                                    </div>
                                </Space>
                            </div>
                        </Radio>

                        <Radio value="summary" style={{ width: '100%' }}>
                            <div style={{ padding: '12px 0' }}>
                                <Space>
                                    <FileTextOutlined style={{ fontSize: 20, color: '#52c41a' }} />
                                    <div>
                                        <div style={{ fontWeight: 500 }}>📋 Summary (Quick Overview)</div>
                                        <div style={{ fontSize: 12, color: '#666', marginTop: 4 }}>
                                            Lightweight summary with:
                                            <ul style={{ margin: '4px 0 0 0', paddingLeft: 20 }}>
                                                <li>Database statistics (table count, row count)</li>
                                                <li>Module breakdown</li>
                                                <li>Key tables and data flow</li>
                                                <li>Critical health issues only</li>
                                            </ul>
                                        </div>
                                        <div style={{ fontSize: 11, color: '#999', marginTop: 4 }}>
                                            Best for: Quick reference, executive summary, status reports
                                        </div>
                                    </div>
                                </Space>
                            </div>
                        </Radio>
                    </Space>
                </Radio.Group>
            </div>

            {exportMutation.isError && (
                <Alert
                    message="Export Failed"
                    description={exportMutation.error?.response?.data?.details || exportMutation.error?.message}
                    type="error"
                    showIcon
                    closable
                    style={{ marginTop: 16 }}
                />
            )}

            <div style={{ marginTop: 16, padding: 12, backgroundColor: '#f5f5f5', borderRadius: 4, fontSize: 12 }}>
                <div style={{ fontWeight: 500, marginBottom: 4 }}>💡 Tips:</div>
                <ul style={{ margin: 0, paddingLeft: 20, color: '#666' }}>
                    <li>Markdown files can be viewed in any text editor or GitHub</li>
                    <li>Use Markdown for version control and collaboration</li>
                    <li>Summary format is perfect for quick status updates</li>
                    <li>Documentation includes AI-generated insights and recommendations</li>
                </ul>
            </div>
        </Modal>
    );
};

export default ExportDocumentationModal;
