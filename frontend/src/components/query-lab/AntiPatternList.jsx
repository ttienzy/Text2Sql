import { Card, List, Tag, Typography, Space, Collapse, Alert, Button, message } from 'antd';
import {
    WarningOutlined,
    CloseCircleOutlined,
    InfoCircleOutlined,
    CheckCircleOutlined,
    ThunderboltOutlined,
    BulbOutlined,
    CopyOutlined,
} from '@ant-design/icons';

const { Text, Paragraph } = Typography;
const { Panel } = Collapse;

const getSeverityConfig = (severity) => {
    switch (severity?.toLowerCase()) {
        case 'critical':
            return {
                color: 'red',
                icon: <CloseCircleOutlined />,
                label: 'Critical',
            };
        case 'warning':
            return {
                color: 'orange',
                icon: <WarningOutlined />,
                label: 'Warning',
            };
        case 'info':
            return {
                color: 'blue',
                icon: <InfoCircleOutlined />,
                label: 'Info',
            };
        default:
            return {
                color: 'green',
                icon: <CheckCircleOutlined />,
                label: 'OK',
            };
    }
};

const AntiPatternList = ({ result }) => {
    if (!result) return null;

    const severityConfig = getSeverityConfig(result.severity);
    const hasIssues = result.detectedIssues && result.detectedIssues.length > 0;
    const hasFixedIssues = result.issuesFixed && result.issuesFixed.length > 0;
    const hasIndexSuggestions = result.indexSuggestions && result.indexSuggestions.length > 0;

    return (
        <div>
            <div style={{ marginBottom: 16, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <Space size="large">
                    <div>
                        <Text type="secondary" style={{ fontSize: 12 }}>Overall Severity</Text>
                        <div>
                            <Tag color={severityConfig.color} icon={severityConfig.icon} style={{ marginTop: 4 }}>
                                {severityConfig.label}
                            </Tag>
                        </div>
                    </div>
                    {result.complexityScore !== undefined && (
                        <div>
                            <Text type="secondary" style={{ fontSize: 12 }}>Complexity Score</Text>
                            <div style={{ marginTop: 4, fontSize: 16, fontWeight: 600 }}>
                                {result.complexityScore}
                            </div>
                        </div>
                    )}
                    {result.modelUsed && (
                        <div>
                            <Text type="secondary" style={{ fontSize: 12 }}>Model Used</Text>
                            <div style={{ marginTop: 4 }}>
                                <Tag color="blue">{result.modelUsed}</Tag>
                            </div>
                        </div>
                    )}
                    {result.estimatedImprovement && (
                        <div>
                            <Text type="secondary" style={{ fontSize: 12 }}>Estimated Improvement</Text>
                            <div style={{ marginTop: 4, color: '#52c41a', fontWeight: 600 }}>
                                <ThunderboltOutlined /> {result.estimatedImprovement}
                            </div>
                        </div>
                    )}
                </Space>
            </div>

            <Collapse
                defaultActiveKey={['issues', 'fixed', 'explanation', 'indexes']}
                style={{ background: '#fff' }}
            >
                {/* Detected Issues */}
                {hasIssues && (
                    <Panel
                        header={
                            <Space>
                                <WarningOutlined style={{ color: '#ff4d4f' }} />
                                <span>Detected Issues ({result.detectedIssues.length})</span>
                            </Space>
                        }
                        key="issues"
                    >
                        <List
                            dataSource={result.detectedIssues}
                            renderItem={(issue) => {
                                const config = getSeverityConfig(issue.severity);
                                return (
                                    <List.Item>
                                        <List.Item.Meta
                                            avatar={
                                                <Tag color={config.color} icon={config.icon}>
                                                    {issue.code}
                                                </Tag>
                                            }
                                            title={
                                                <Space>
                                                    <Text strong>{issue.title}</Text>
                                                    {issue.location && (
                                                        <Text type="secondary" style={{ fontSize: 12 }}>
                                                            (Line {issue.location})
                                                        </Text>
                                                    )}
                                                </Space>
                                            }
                                            description={
                                                <div>
                                                    <Paragraph style={{ marginBottom: 4 }}>
                                                        {issue.description}
                                                    </Paragraph>
                                                    {issue.impact && (
                                                        <Text type="danger" style={{ fontSize: 12 }}>
                                                            <WarningOutlined /> Impact: {issue.impact}
                                                        </Text>
                                                    )}
                                                </div>
                                            }
                                        />
                                    </List.Item>
                                );
                            }}
                        />
                    </Panel>
                )}

                {/* Fixed Issues */}
                {hasFixedIssues && (
                    <Panel
                        header={
                            <Space>
                                <CheckCircleOutlined style={{ color: '#52c41a' }} />
                                <span>Issues Fixed ({result.issuesFixed.length})</span>
                            </Space>
                        }
                        key="fixed"
                    >
                        <List
                            dataSource={result.issuesFixed}
                            renderItem={(fix) => (
                                <List.Item>
                                    <List.Item.Meta
                                        avatar={<CheckCircleOutlined style={{ color: '#52c41a', fontSize: 20 }} />}
                                        description={<Text>{fix}</Text>}
                                    />
                                </List.Item>
                            )}
                        />
                    </Panel>
                )}

                {/* Explanation */}
                {result.explanation && (
                    <Panel
                        header={
                            <Space>
                                <InfoCircleOutlined style={{ color: '#1890ff' }} />
                                <span>Explanation (Vietnamese)</span>
                            </Space>
                        }
                        key="explanation"
                    >
                        <Alert
                            message={result.explanation}
                            type="info"
                            showIcon
                            icon={<BulbOutlined />}
                        />
                    </Panel>
                )}

                {/* Index Suggestions */}
                {hasIndexSuggestions && (
                    <Panel
                        header={
                            <Space>
                                <BulbOutlined style={{ color: '#faad14' }} />
                                <span>Index Suggestions ({result.indexSuggestions.length})</span>
                            </Space>
                        }
                        key="indexes"
                    >
                        <List
                            dataSource={result.indexSuggestions}
                            renderItem={(suggestion) => (
                                <List.Item
                                    actions={[
                                        <Button
                                            size="small"
                                            icon={<CopyOutlined />}
                                            onClick={() => {
                                                navigator.clipboard.writeText(suggestion);
                                                message.success('DDL copied to clipboard');
                                            }}
                                        >
                                            Copy DDL
                                        </Button>
                                    ]}
                                >
                                    <List.Item.Meta
                                        avatar={<BulbOutlined style={{ color: '#faad14', fontSize: 20 }} />}
                                        description={
                                            <pre style={{
                                                background: '#f5f5f5',
                                                padding: 8,
                                                borderRadius: 4,
                                                fontSize: 12,
                                                margin: 0,
                                                overflow: 'auto'
                                            }}>
                                                {suggestion}
                                            </pre>
                                        }
                                    />
                                </List.Item>
                            )}
                        />
                    </Panel>
                )}
            </Collapse>
        </div>
    );
};

export default AntiPatternList;
