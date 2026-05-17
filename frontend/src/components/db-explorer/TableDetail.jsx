import { useState } from 'react';
import { Card, Descriptions, Table, Tag, Space, Button, Empty, Spin, Tabs, Modal, message, Tooltip, Progress, Alert, Input, Select, Checkbox } from 'antd';
import {
    TableOutlined, KeyOutlined, LinkOutlined, DatabaseOutlined, MessageOutlined,
    EyeOutlined, CopyOutlined, StarOutlined, StarFilled, WarningOutlined, ArrowRightOutlined,
    ThunderboltOutlined, BulbOutlined, RobotOutlined, EditOutlined
} from '@ant-design/icons';
import { formatNumber } from '../../utils/formatters';
import { useSampleDataQuery, useSemanticProfileQuery } from '../../api/dbExplorer/queries';
import { useAnalyzeTableDetailMutation, useSaveSemanticProfileMutation } from '../../api/dbExplorer/commands';
import QuerySuggestions from './QuerySuggestions';

const ROLE_COLORS = {
    Master: '#1890ff',
    Transaction: '#52c41a',
    Bridge: '#faad14',
    Configuration: '#722ed1',
    LogAudit: '#8c8c8c',
};

const ROLE_ICONS = {
    Master: '🏷️',
    Transaction: '💳',
    Bridge: '🔗',
    Configuration: '⚙️',
    LogAudit: '📝',
};

const TableDetail = ({ table, loading, onQueryTable, onJumpToTable, pinnedTables = [], onTogglePin }) => {
    const [sampleModalVisible, setSampleModalVisible] = useState(false);
    const [enableSampleQuery, setEnableSampleQuery] = useState(false);
    const [tableAnalysis, setTableAnalysis] = useState(null);
    const [semanticModalVisible, setSemanticModalVisible] = useState(false);
    const [tableDraft, setTableDraft] = useState({ description: '', businessMeaning: '', synonyms: '' });
    const [columnDrafts, setColumnDrafts] = useState([]);

    // Sample data query
    const { data: sampleData, isLoading: sampleLoading } = useSampleDataQuery(
        table?.connectionId,
        table?.tableName,
        { enabled: enableSampleQuery }
    );

    const { data: semanticProfile } = useSemanticProfileQuery(
        table?.connectionId,
        { enabled: !!table?.connectionId }
    );

    // Table detail analysis mutation
    const analyzeTableMutation = useAnalyzeTableDetailMutation({
        onSuccess: (data) => {
            setTableAnalysis(data);
            message.success('Table analysis completed!');
        },
        onError: (error) => {
            message.error(`Analysis failed: ${error.response?.data?.details || error.message}`);
        },
    });

    const saveSemanticProfileMutation = useSaveSemanticProfileMutation({
        onSuccess: (data) => {
            setSemanticModalVisible(false);
            message.success(data?.requiresReindex
                ? 'Semantic profile saved. Re-index schema when you want Qdrant search to reflect it.'
                : 'Semantic profile saved.');
        },
        onError: (error) => {
            message.error(`Save failed: ${error.response?.data?.details || error.message}`);
        },
    });

    if (loading) {
        return (
            <div style={{ textAlign: 'center', padding: '40px 0' }}>
                <Spin size="large" />
            </div>
        );
    }

    if (!table) {
        return (
            <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Select a table to view details"
                style={{ marginTop: 100 }}
            />
        );
    }

    const isPinned = pinnedTables.includes(table.tableName);
    const currentTableProfile = semanticProfile?.tables?.find(
        t => t.tableName?.toLowerCase() === table.tableName.toLowerCase()
    );

    // Handle sample data button click
    const handleViewSampleData = () => {
        setEnableSampleQuery(true);
        setSampleModalVisible(true);
    };

    // Handle analyze table
    const handleAnalyzeTable = () => {
        if (table?.connectionId && table?.tableName) {
            analyzeTableMutation.mutate({
                connectionId: table.connectionId,
                tableName: table.tableName,
            });
        }
    };

    // Copy to clipboard helper
    const copyToClipboard = (text, label) => {
        navigator.clipboard.writeText(text);
        message.success(`${label} copied to clipboard`);
    };

    const splitSynonyms = (value) => (value || '')
        .split(',')
        .map(v => v.trim())
        .filter(Boolean);

    const openSemanticModal = () => {
        setTableDraft({
            description: currentTableProfile?.description || table.description || '',
            businessMeaning: currentTableProfile?.businessMeaning || table.businessMeaning || '',
            synonyms: (currentTableProfile?.synonyms || table.synonyms || []).join(', '),
        });

        setColumnDrafts((table.columns || []).map(column => {
            const columnProfile = currentTableProfile?.columns?.find(
                c => c.columnName?.toLowerCase() === column.columnName.toLowerCase()
            );

            return {
                columnName: column.columnName,
                dataType: column.dataType,
                description: columnProfile?.description || column.description || '',
                businessMeaning: columnProfile?.businessMeaning || column.businessMeaning || '',
                role: columnProfile?.role || column.role || '',
                displayPriority: columnProfile?.displayPriority || column.displayPriority || '',
                preferredForReports: columnProfile?.preferredForReports ?? column.preferredForReports ?? false,
                synonyms: (columnProfile?.synonyms || column.synonyms || []).join(', '),
            };
        }));

        setSemanticModalVisible(true);
    };

    const updateColumnDraft = (columnName, patch) => {
        setColumnDrafts(prev => prev.map(column =>
            column.columnName === columnName ? { ...column, ...patch } : column
        ));
    };

    const handleSaveSemanticProfile = () => {
        const cleanedColumns = columnDrafts
            .map(column => ({
                columnName: column.columnName,
                description: column.description?.trim() || null,
                businessMeaning: column.businessMeaning?.trim() || null,
                role: column.role || null,
                displayPriority: column.displayPriority || null,
                preferredForReports: column.preferredForReports,
                synonyms: splitSynonyms(column.synonyms),
            }))
            .filter(column =>
                column.description ||
                column.businessMeaning ||
                column.role ||
                column.displayPriority ||
                column.preferredForReports ||
                column.synonyms.length > 0
            );

        const nextTableProfile = {
            tableName: table.tableName,
            description: tableDraft.description?.trim() || null,
            businessMeaning: tableDraft.businessMeaning?.trim() || null,
            synonyms: splitSynonyms(tableDraft.synonyms),
            columns: cleanedColumns,
        };

        const nextTables = [
            ...(semanticProfile?.tables || []).filter(
                t => t.tableName?.toLowerCase() !== table.tableName.toLowerCase()
            ),
            nextTableProfile,
        ].filter(t =>
            t.description ||
            t.businessMeaning ||
            t.synonyms?.length > 0 ||
            t.columns?.length > 0
        );

        saveSemanticProfileMutation.mutate({
            connectionId: table.connectionId,
            profile: {
                ...(semanticProfile || {}),
                connectionId: table.connectionId,
                tables: nextTables,
            },
        });
    };

    // Generate DDL
    const generateDDL = () => {
        const columns = table.columns.map(col => {
            let def = `  [${col.columnName}] ${col.dataType}`;
            if (col.maxLength) def += `(${col.maxLength})`;
            if (!col.isNullable) def += ' NOT NULL';
            return def;
        }).join(',\n');

        const pks = table.columns.filter(c => c.isPrimaryKey).map(c => c.columnName);
        const pkConstraint = pks.length > 0 ? `,\n  PRIMARY KEY (${pks.join(', ')})` : '';

        return `CREATE TABLE [${table.schema}].[${table.tableName}] (\n${columns}${pkConstraint}\n);`;
    };

    // Generate SELECT statement
    const generateSelect = () => {
        const columns = table.columns.map(c => `  [${c.columnName}]`).join(',\n');
        return `SELECT\n${columns}\nFROM [${table.schema}].[${table.tableName}];`;
    };

    // Columns table configuration
    const columnColumns = [
        {
            title: 'Column',
            dataIndex: 'columnName',
            key: 'columnName',
            width: '25%',
            render: (text, record) => {
                const interpretation = tableAnalysis?.columnInterpretations?.find(
                    ci => ci.columnName === text
                );

                return (
                    <Space direction="vertical" size={0}>
                        <Space>
                            {record.isPrimaryKey && <Tooltip title="Primary Key"><KeyOutlined style={{ color: '#faad14' }} /></Tooltip>}
                            {record.isForeignKey && <Tooltip title="Foreign Key"><LinkOutlined style={{ color: '#1890ff' }} /></Tooltip>}
                            <span style={{ fontWeight: record.isPrimaryKey ? 500 : 'normal' }}>{text}</span>
                        </Space>
                        {interpretation && (
                            <Tooltip title={interpretation.description}>
                                <div style={{ fontSize: 11, color: '#1890ff', marginTop: 2 }}>
                                    <BulbOutlined /> {interpretation.vietnamese}
                                    {interpretation.english && ` (${interpretation.english})`}
                                </div>
                            </Tooltip>
                        )}
                    </Space>
                );
            },
        },
        {
            title: 'Type',
            dataIndex: 'dataType',
            key: 'dataType',
            width: '20%',
            render: (text, record) => (
                <span>
                    {text}
                    {record.maxLength && `(${record.maxLength})`}
                </span>
            ),
        },
        {
            title: 'Nullable',
            dataIndex: 'isNullable',
            key: 'isNullable',
            width: '15%',
            render: (nullable) => (
                <Tag color={nullable ? 'default' : 'blue'}>
                    {nullable ? 'NULL' : 'NOT NULL'}
                </Tag>
            ),
        },
        {
            title: 'Stats',
            key: 'stats',
            width: '25%',
            render: (_, record) => {
                if (!record.statistics) return '-';
                const nullRate = record.statistics.nullRate * 100;
                const isHighNull = nullRate > 80;

                return (
                    <Space direction="vertical" size={2} style={{ width: '100%' }}>
                        {record.statistics.nullRate > 0 && (
                            <div>
                                {isHighNull && <WarningOutlined style={{ color: '#ff4d4f', marginRight: 4 }} />}
                                <span style={{ fontSize: 12, color: isHighNull ? '#ff4d4f' : '#999' }}>
                                    Null: {nullRate.toFixed(1)}%
                                </span>
                                <Progress
                                    percent={nullRate}
                                    size="small"
                                    showInfo={false}
                                    strokeColor={isHighNull ? '#ff4d4f' : '#d9d9d9'}
                                    style={{ marginTop: 2 }}
                                />
                            </div>
                        )}
                        {record.statistics.distinctCount > 0 && (
                            <span style={{ fontSize: 12, color: '#999' }}>
                                Distinct: {formatNumber(record.statistics.distinctCount)}
                            </span>
                        )}
                    </Space>
                );
            },
        },
        {
            title: 'Actions',
            key: 'actions',
            width: '10%',
            render: (_, record) => (
                <Space>
                    <Tooltip title="Copy column name">
                        <Button
                            type="text"
                            size="small"
                            icon={<CopyOutlined />}
                            onClick={() => copyToClipboard(record.columnName, 'Column name')}
                        />
                    </Tooltip>
                    {record.isForeignKey && record.referencedTable && (
                        <Tooltip title={`Jump to ${record.referencedTable}`}>
                            <Button
                                type="text"
                                size="small"
                                icon={<ArrowRightOutlined />}
                                onClick={() => onJumpToTable(record.referencedTable)}
                            />
                        </Tooltip>
                    )}
                </Space>
            ),
        },
    ];

    const semanticColumnColumns = [
        {
            title: 'Column',
            dataIndex: 'columnName',
            key: 'columnName',
            width: 180,
            render: (text, record) => (
                <Space direction="vertical" size={0}>
                    <Space>
                        <span style={{ fontWeight: 500 }}>{text}</span>
                        {record.preferredForReports && <Tag color="green">report</Tag>}
                    </Space>
                    <span style={{ fontSize: 12, color: '#999' }}>{record.dataType}</span>
                </Space>
            ),
        },
        {
            title: 'Meaning',
            key: 'meaning',
            width: 320,
            render: (_, record) => (
                <Space direction="vertical" size={6} style={{ width: '100%' }}>
                    <Input
                        placeholder="Description"
                        value={record.description}
                        onChange={(event) => updateColumnDraft(record.columnName, { description: event.target.value })}
                    />
                    <Input
                        placeholder="Business meaning"
                        value={record.businessMeaning}
                        onChange={(event) => updateColumnDraft(record.columnName, { businessMeaning: event.target.value })}
                    />
                    <Input
                        placeholder="Synonyms, comma separated"
                        value={record.synonyms}
                        onChange={(event) => updateColumnDraft(record.columnName, { synonyms: event.target.value })}
                    />
                </Space>
            ),
        },
        {
            title: 'Semantics',
            key: 'semantics',
            width: 260,
            render: (_, record) => (
                <Space direction="vertical" size={8} style={{ width: '100%' }}>
                    <Select
                        allowClear
                        placeholder="Role"
                        value={record.role || undefined}
                        onChange={(value) => updateColumnDraft(record.columnName, { role: value || '' })}
                        options={[
                            { value: 'display_label', label: 'Display label' },
                            { value: 'business_metric', label: 'Business metric' },
                            { value: 'time_dimension', label: 'Time dimension' },
                            { value: 'attribute', label: 'Attribute' },
                            { value: 'technical_key', label: 'Technical key' },
                            { value: 'audit_field', label: 'Audit field' },
                            { value: 'internal_flag', label: 'Internal flag' },
                        ]}
                        style={{ width: '100%' }}
                    />
                    <Select
                        allowClear
                        placeholder="Display priority"
                        value={record.displayPriority || undefined}
                        onChange={(value) => updateColumnDraft(record.columnName, { displayPriority: value || '' })}
                        options={[
                            { value: 'high', label: 'High' },
                            { value: 'medium', label: 'Medium' },
                            { value: 'low', label: 'Low' },
                        ]}
                        style={{ width: '100%' }}
                    />
                    <Checkbox
                        checked={record.preferredForReports}
                        onChange={(event) => updateColumnDraft(record.columnName, { preferredForReports: event.target.checked })}
                    >
                        Preferred for reports
                    </Checkbox>
                </Space>
            ),
        },
    ];

    // Relationships table configuration
    const relationshipColumns = [
        {
            title: 'Direction',
            dataIndex: 'direction',
            key: 'direction',
            render: (direction) => (
                <Tag color={direction === 'outgoing' ? 'blue' : 'green'}>
                    {direction === 'outgoing' ? 'FK →' : '← Referenced by'}
                </Tag>
            ),
        },
        {
            title: 'Related Table',
            dataIndex: 'relatedTable',
            key: 'relatedTable',
            render: (text) => (
                <Space>
                    <TableOutlined />
                    <span>{text}</span>
                </Space>
            ),
        },
        {
            title: 'Via Column',
            dataIndex: 'viaColumn',
            key: 'viaColumn',
        },
        {
            title: 'Type',
            dataIndex: 'type',
            key: 'type',
        },
        {
            title: 'Actions',
            key: 'actions',
            render: (_, record) => (
                <Button
                    type="link"
                    size="small"
                    icon={<ArrowRightOutlined />}
                    onClick={() => onJumpToTable(record.relatedTable)}
                >
                    Jump
                </Button>
            ),
        },
    ];

    // Indexes table configuration
    const indexColumns = [
        {
            title: 'Index Name',
            dataIndex: 'indexName',
            key: 'indexName',
            render: (text, record) => (
                <Space>
                    {record.isPrimaryKey && <KeyOutlined style={{ color: '#faad14' }} />}
                    <span>{text}</span>
                </Space>
            ),
        },
        {
            title: 'Columns',
            dataIndex: 'columns',
            key: 'columns',
            render: (columns) => columns.join(', '),
        },
        {
            title: 'Type',
            key: 'type',
            render: (_, record) => (
                <Space>
                    {record.isPrimaryKey && <Tag color="gold">Primary Key</Tag>}
                    {record.isUnique && <Tag color="blue">Unique</Tag>}
                    {!record.isPrimaryKey && !record.isUnique && <Tag>Index</Tag>}
                </Space>
            ),
        },
    ];

    const tabItems = [
        {
            key: 'columns',
            label: `Columns (${table.columns?.length || 0})`,
            children: (
                <div>
                    {!tableAnalysis && (
                        <Alert
                            message="AI Analysis Available"
                            description={
                                <Space>
                                    <span>Get AI-powered column interpretations and implicit relationship detection</span>
                                    <Button
                                        type="primary"
                                        size="small"
                                        icon={<RobotOutlined />}
                                        onClick={handleAnalyzeTable}
                                        loading={analyzeTableMutation.isPending}
                                    >
                                        Analyze Table
                                    </Button>
                                </Space>
                            }
                            type="info"
                            showIcon
                            style={{ marginBottom: 16 }}
                        />
                    )}
                    <Table
                        dataSource={table.columns}
                        columns={columnColumns}
                        rowKey="columnName"
                        pagination={false}
                        size="small"
                    />
                </div>
            ),
        },
        {
            key: 'ai-insights',
            label: (
                <span>
                    <RobotOutlined /> AI Insights
                    {tableAnalysis && <Tag color="green" style={{ marginLeft: 8 }}>Analyzed</Tag>}
                </span>
            ),
            children: tableAnalysis ? (
                <Space direction="vertical" size="large" style={{ width: '100%' }}>
                    {/* Column Interpretations */}
                    {tableAnalysis.columnInterpretations?.length > 0 && (
                        <Card title="Column Interpretations" size="small">
                            <Table
                                dataSource={tableAnalysis.columnInterpretations}
                                columns={[
                                    {
                                        title: 'Column',
                                        dataIndex: 'columnName',
                                        key: 'columnName',
                                        width: '20%',
                                    },
                                    {
                                        title: 'Vietnamese',
                                        dataIndex: 'vietnamese',
                                        key: 'vietnamese',
                                        width: '25%',
                                    },
                                    {
                                        title: 'English',
                                        dataIndex: 'english',
                                        key: 'english',
                                        width: '25%',
                                    },
                                    {
                                        title: 'Description',
                                        dataIndex: 'description',
                                        key: 'description',
                                        width: '25%',
                                    },
                                    {
                                        title: 'Confidence',
                                        dataIndex: 'confidence',
                                        key: 'confidence',
                                        width: '5%',
                                        render: (conf) => (
                                            <Tag color={conf > 0.8 ? 'green' : conf > 0.6 ? 'orange' : 'red'}>
                                                {(conf * 100).toFixed(0)}%
                                            </Tag>
                                        ),
                                    },
                                ]}
                                rowKey="columnName"
                                pagination={false}
                                size="small"
                            />
                        </Card>
                    )}

                    {/* Implicit Relationships */}
                    {tableAnalysis.implicitRelationships?.length > 0 && (
                        <Card title="Implicit Relationships Detected" size="small">
                            <Table
                                dataSource={tableAnalysis.implicitRelationships}
                                columns={[
                                    {
                                        title: 'From',
                                        key: 'from',
                                        render: (_, record) => `${record.fromTable}.${record.fromColumn}`,
                                    },
                                    {
                                        title: 'To',
                                        key: 'to',
                                        render: (_, record) => `${record.toTable}.${record.toColumn}`,
                                    },
                                    {
                                        title: 'Method',
                                        dataIndex: 'detectionMethod',
                                        key: 'detectionMethod',
                                        render: (method) => <Tag>{method}</Tag>,
                                    },
                                    {
                                        title: 'Reason',
                                        dataIndex: 'reason',
                                        key: 'reason',
                                    },
                                    {
                                        title: 'Confidence',
                                        dataIndex: 'confidence',
                                        key: 'confidence',
                                        render: (conf) => (
                                            <Tag color={conf > 0.8 ? 'green' : conf > 0.6 ? 'orange' : 'red'}>
                                                {(conf * 100).toFixed(0)}%
                                            </Tag>
                                        ),
                                    },
                                ]}
                                rowKey={(record) => `${record.fromColumn}-${record.toTable}`}
                                pagination={false}
                                size="small"
                            />
                        </Card>
                    )}

                    {/* Health Issues */}
                    {tableAnalysis.healthIssues?.length > 0 && (
                        <Card title="Health Issues" size="small">
                            <Space direction="vertical" style={{ width: '100%' }}>
                                {tableAnalysis.healthIssues.map((issue, idx) => (
                                    <Alert
                                        key={idx}
                                        message={issue.description}
                                        description={issue.recommendation}
                                        type={issue.severity === 'critical' ? 'error' : issue.severity === 'warning' ? 'warning' : 'info'}
                                        showIcon
                                    />
                                ))}
                            </Space>
                        </Card>
                    )}

                    {tableAnalysis.columnInterpretations?.length === 0 &&
                        tableAnalysis.implicitRelationships?.length === 0 &&
                        tableAnalysis.healthIssues?.length === 0 && (
                            <Empty description="No AI insights available for this table" />
                        )}
                </Space>
            ) : (
                <div style={{ textAlign: 'center', padding: '40px 0' }}>
                    <RobotOutlined style={{ fontSize: 48, color: '#d9d9d9', marginBottom: 16 }} />
                    <div style={{ fontSize: 16, marginBottom: 8 }}>AI Analysis Not Run</div>
                    <div style={{ color: '#999', marginBottom: 24 }}>
                        Click "Analyze Table" to get AI-powered insights including:
                        <ul style={{ textAlign: 'left', display: 'inline-block', marginTop: 8 }}>
                            <li>Column name interpretations (Vietnamese + English)</li>
                            <li>Implicit foreign key detection</li>
                            <li>Table-specific health issues</li>
                        </ul>
                    </div>
                    <Button
                        type="primary"
                        size="large"
                        icon={<RobotOutlined />}
                        onClick={handleAnalyzeTable}
                        loading={analyzeTableMutation.isPending}
                    >
                        Analyze Table with AI
                    </Button>
                    <div style={{ marginTop: 12, fontSize: 12, color: '#999' }}>
                        ⚡ Analysis takes ~3 seconds
                    </div>
                </div>
            ),
        },
        {
            key: 'suggestions',
            label: (
                <span>
                    <ThunderboltOutlined /> Suggestions
                </span>
            ),
            children: (
                <QuerySuggestions
                    connectionId={table.connectionId}
                    tableName={table.tableName}
                />
            ),
        },
        {
            key: 'relationships',
            label: `Relationships (${table.relationships?.length || 0})`,
            children: table.relationships?.length > 0 ? (
                <Table
                    dataSource={table.relationships}
                    columns={relationshipColumns}
                    rowKey={(record) => `${record.direction}-${record.relatedTable}-${record.viaColumn}`}
                    pagination={false}
                    size="small"
                />
            ) : (
                <Empty description="No relationships" />
            ),
        },
        {
            key: 'indexes',
            label: `Indexes (${table.indexes?.length || 0})`,
            children: table.indexes?.length > 0 ? (
                <Table
                    dataSource={table.indexes}
                    columns={indexColumns}
                    rowKey="indexName"
                    pagination={false}
                    size="small"
                />
            ) : (
                <Empty description="No indexes" />
            ),
        },
    ];

    return (
        <div style={{ height: '100%', overflow: 'auto', padding: 16 }}>
            <Card
                title={
                    <Space>
                        <span style={{ fontSize: 20 }}>📋</span>
                        <TableOutlined />
                        <span>{table.tableName}</span>
                        <Tag color={ROLE_COLORS[table.role]}>
                            {ROLE_ICONS[table.role]} {table.role}
                        </Tag>
                        {table.module && (
                            <Tag color="purple">📦 {table.module}</Tag>
                        )}
                        {(table.hasSemanticOverride || currentTableProfile) && (
                            <Tag color="cyan">Semantic override</Tag>
                        )}
                    </Space>
                }
                extra={
                    <Space>
                        <Tooltip title={isPinned ? 'Unpin table' : 'Pin table'}>
                            <Button
                                type="text"
                                icon={isPinned ? <StarFilled style={{ color: '#faad14' }} /> : <StarOutlined />}
                                onClick={() => onTogglePin(table.tableName)}
                            />
                        </Tooltip>
                        <Button
                            icon={<EyeOutlined />}
                            onClick={handleViewSampleData}
                        >
                            Sample Data
                        </Button>
                        <Button
                            icon={<CopyOutlined />}
                            onClick={() => copyToClipboard(generateDDL(), 'DDL')}
                        >
                            Copy DDL
                        </Button>
                        <Button
                            icon={<CopyOutlined />}
                            onClick={() => copyToClipboard(generateSelect(), 'SELECT')}
                        >
                            Copy SELECT
                        </Button>
                        <Button
                            icon={<EditOutlined />}
                            onClick={openSemanticModal}
                        >
                            Semantic Profile
                        </Button>
                        <Button.Group>
                            <Button
                                type="primary"
                                icon={<MessageOutlined />}
                                onClick={() => onQueryTable(table, 'query')}
                            >
                                Query
                            </Button>
                            <Button
                                type="primary"
                                icon={<MessageOutlined />}
                                onClick={() => onQueryTable(table, 'relationships')}
                            >
                                Explain Relations
                            </Button>
                            <Button
                                type="primary"
                                icon={<WarningOutlined />}
                                onClick={() => onQueryTable(table, 'quality')}
                            >
                                Check Quality
                            </Button>
                        </Button.Group>
                    </Space>
                }
            >
                <Descriptions column={2} size="small" style={{ marginBottom: 16 }}>
                    <Descriptions.Item label="Schema">{table.schema}</Descriptions.Item>
                    <Descriptions.Item label="Module">{table.module || '-'}</Descriptions.Item>
                    <Descriptions.Item label="Row Count">{formatNumber(table.rowCount)}</Descriptions.Item>
                    <Descriptions.Item label="Columns">{table.columns?.length || 0}</Descriptions.Item>
                </Descriptions>

                {table.description && (
                    <div style={{ marginBottom: 16, padding: 12, backgroundColor: '#f5f5f5', borderRadius: 4 }}>
                        <div style={{ fontWeight: 500, marginBottom: 4, color: '#666' }}>Description:</div>
                        <div style={{ color: '#666', fontStyle: 'italic' }}>"{table.description}"</div>
                    </div>
                )}

                {(table.businessMeaning || table.synonyms?.length > 0) && (
                    <div style={{ marginBottom: 16, padding: 12, backgroundColor: '#f6ffed', border: '1px solid #b7eb8f', borderRadius: 4 }}>
                        {table.businessMeaning && (
                            <>
                                <div style={{ fontWeight: 500, marginBottom: 4, color: '#389e0d' }}>Business meaning:</div>
                                <div style={{ color: '#389e0d', marginBottom: table.synonyms?.length > 0 ? 8 : 0 }}>
                                    {table.businessMeaning}
                                </div>
                            </>
                        )}
                        {table.synonyms?.length > 0 && (
                            <Space wrap>
                                {table.synonyms.map(synonym => <Tag key={synonym} color="green">{synonym}</Tag>)}
                            </Space>
                        )}
                    </div>
                )}

                <Tabs items={tabItems} />
            </Card>

            {/* Sample Data Modal */}
            <Modal
                title={`Sample Data - ${table.tableName}`}
                open={sampleModalVisible}
                onCancel={() => setSampleModalVisible(false)}
                width={1000}
                footer={[
                    <Button key="close" onClick={() => setSampleModalVisible(false)}>
                        Close
                    </Button>
                ]}
            >
                {sampleLoading ? (
                    <div style={{ textAlign: 'center', padding: '40px 0' }}>
                        <Spin />
                        <div style={{ marginTop: 16 }}>Loading sample data...</div>
                    </div>
                ) : sampleData ? (
                    <Table
                        dataSource={sampleData.rows}
                        columns={sampleData.columns.map(col => ({
                            title: col,
                            dataIndex: col,
                            key: col,
                            render: (value) => value === null ? <Tag>NULL</Tag> : String(value)
                        }))}
                        pagination={false}
                        size="small"
                        scroll={{ x: 'max-content' }}
                    />
                ) : (
                    <Empty description="No sample data available" />
                )}
            </Modal>

            <Modal
                title={`Semantic Profile - ${table.tableName}`}
                open={semanticModalVisible}
                onCancel={() => setSemanticModalVisible(false)}
                width={1100}
                okText="Save to Redis"
                onOk={handleSaveSemanticProfile}
                confirmLoading={saveSemanticProfileMutation.isPending}
            >
                <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                    <Alert
                        type="info"
                        showIcon
                        message="These metadata edits are stored in Redis and reused by SQL generation, schema cache, and the next Qdrant re-index."
                    />
                    <Space direction="vertical" size={8} style={{ width: '100%' }}>
                        <Input
                            placeholder="Table description"
                            value={tableDraft.description}
                            onChange={(event) => setTableDraft(prev => ({ ...prev, description: event.target.value }))}
                        />
                        <Input.TextArea
                            rows={2}
                            placeholder="Business meaning, for example: ReviewDate is the review date, not the sales date"
                            value={tableDraft.businessMeaning}
                            onChange={(event) => setTableDraft(prev => ({ ...prev, businessMeaning: event.target.value }))}
                        />
                        <Input
                            placeholder="Table synonyms, comma separated"
                            value={tableDraft.synonyms}
                            onChange={(event) => setTableDraft(prev => ({ ...prev, synonyms: event.target.value }))}
                        />
                    </Space>
                    <Table
                        dataSource={columnDrafts}
                        columns={semanticColumnColumns}
                        rowKey="columnName"
                        pagination={false}
                        size="small"
                        scroll={{ y: 420 }}
                    />
                </Space>
            </Modal>
        </div>
    );
};

export default TableDetail;
