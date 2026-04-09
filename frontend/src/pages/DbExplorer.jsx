import { useState, useEffect } from 'react';
import { Layout, message, Spin, Alert, Button, Card, Descriptions, Badge, Space, Tabs } from 'antd';
import { useNavigate } from 'react-router-dom';
import { DatabaseOutlined, ThunderboltOutlined, CheckCircleOutlined, ApartmentOutlined } from '@ant-design/icons';
import useConnectionStore from '../store/connectionStore';
import {
    useConnectionInfoQuery,
    useStatusQuery,
    useOverviewQuery,
    useTablesQuery,
    useTableDetailQuery,
    useHealthQuery,
    useGraphQuery,
    useSchemaChangesQuery,
    useAnalyzeMutation,
} from '../api/dbExplorer';
import {
    DatabaseOverviewCard,
    TableList,
    TableDetail,
    HealthReport,
    ERDiagramView,
    SchemaChangesModal,
    SemanticSearch,
    ExportDocumentationModal,
    IndexRecommendationReport,
    NamingConventionReport,
} from '../components/db-explorer';

const { Sider, Content } = Layout;

const DbExplorer = () => {
    const navigate = useNavigate();
    const { activeConnection } = useConnectionStore();
    const [selectedTable, setSelectedTable] = useState(null);
    const [healthModalVisible, setHealthModalVisible] = useState(false);
    const [changesModalVisible, setChangesModalVisible] = useState(false);
    const [exportModalVisible, setExportModalVisible] = useState(false);
    const [indexRecommendationsVisible, setIndexRecommendationsVisible] = useState(false);
    const [namingAnalysisVisible, setNamingAnalysisVisible] = useState(false);
    const [selectedModule, setSelectedModule] = useState(null);
    const [activeTab, setActiveTab] = useState('tables'); // 'tables' or 'graph'
    const [pinnedTables, setPinnedTables] = useState(() => {
        // Load pinned tables from localStorage
        const saved = localStorage.getItem('pinnedTables');
        return saved ? JSON.parse(saved) : [];
    });

    // Save pinned tables to localStorage
    useEffect(() => {
        localStorage.setItem('pinnedTables', JSON.stringify(pinnedTables));
    }, [pinnedTables]);

    // Get connection info from API
    const {
        data: connectionInfo,
        isLoading: connectionInfoLoading,
    } = useConnectionInfoQuery(activeConnection?.id);

    // Check status first
    const {
        data: status,
        isLoading: statusLoading,
        refetch: refetchStatus,
    } = useStatusQuery(activeConnection?.id);

    // Queries - only enabled if data exists
    const {
        data: overview,
        isLoading: overviewLoading,
        error: overviewError,
        refetch: refetchOverview,
    } = useOverviewQuery(activeConnection?.id, {
        enabled: status?.hasData === true,
    });

    const {
        data: tablesData,
        isLoading: tablesLoading,
    } = useTablesQuery(activeConnection?.id, { module: selectedModule }, {
        enabled: status?.hasData === true,
    });

    const {
        data: tableDetail,
        isLoading: tableDetailLoading,
    } = useTableDetailQuery(activeConnection?.id, selectedTable?.tableName, {
        enabled: status?.hasData === true && !!selectedTable,
    });

    const {
        data: health,
        isLoading: healthLoading,
    } = useHealthQuery(activeConnection?.id, {
        enabled: status?.hasData === true,
    });

    const {
        data: graphData,
        isLoading: graphLoading,
    } = useGraphQuery(activeConnection?.id, {
        enabled: status?.hasData === true,
    });

    const {
        data: schemaChanges,
        isLoading: changesLoading,
        refetch: refetchChanges,
    } = useSchemaChangesQuery(activeConnection?.id, {
        enabled: status?.hasData === true && changesModalVisible,
    });

    // Mutations
    const analyzeMutation = useAnalyzeMutation({
        mode: 'overview', // Use lightweight overview mode for fast initial load
        onSuccess: (data) => {
            const modeMsg = data?.mode === 'overview' ? ' (fast overview)' : '';
            const usedQdrant = data?.usedQdrant ? ' (optimized with Qdrant)' : '';
            message.success(`Database analysis completed successfully!${modeMsg}${usedQdrant}`);
            refetchStatus();
            refetchOverview();
        },
        onError: (error) => {
            const errorMsg = error.response?.data?.details || error.message || 'Failed to analyze database';
            message.error(`Analysis failed: ${errorMsg}`);
        },
    });

    // Handlers
    const handleAnalyze = () => {
        analyzeMutation.mutate(activeConnection?.id);
    };

    const handleTableSelect = (table) => {
        setSelectedTable(table);
    };

    const handleViewHealth = () => {
        setHealthModalVisible(true);
    };

    const handleViewChanges = () => {
        setChangesModalVisible(true);
        refetchChanges();
    };

    const handleExport = () => {
        setExportModalVisible(true);
    };

    const handleViewIndexRecommendations = () => {
        setIndexRecommendationsVisible(true);
    };

    const handleViewNamingAnalysis = () => {
        setNamingAnalysisVisible(true);
    };

    const handleReAnalyzeFromChanges = () => {
        setChangesModalVisible(false);
        handleAnalyze();
    };

    const handleQueryTable = (table, contextType = 'query') => {
        let contextMessage = '';

        switch (contextType) {
            case 'query':
                contextMessage = `I want to query the ${table.tableName} table. ${table.description || ''}`;
                break;
            case 'relationships':
                const relCount = table.relationships?.length || tableDetail?.relationships?.length || 0;
                const relTables = table.relationships?.map(r => r.relatedTable).join(', ') ||
                    tableDetail?.relationships?.map(r => r.relatedTable).join(', ') || 'other tables';
                contextMessage = `Explain the relationships of ${table.tableName} table. It has ${relCount} relationships with: ${relTables}. ${table.description || ''}`;
                break;
            case 'quality':
                const colCount = table.columns?.length || tableDetail?.columns?.length || 0;
                const nullCols = tableDetail?.columns?.filter(c => c.statistics?.nullRate > 0.5).length || 0;
                contextMessage = `Analyze data quality issues in ${table.tableName} table. It has ${colCount} columns${nullCols > 0 ? `, ${nullCols} columns with high null rates` : ''}. Check for missing indexes, high null rates, and data integrity issues.`;
                break;
            default:
                contextMessage = `I want to query the ${table.tableName} table.`;
        }

        navigate('/chat', {
            state: {
                contextTable: table.tableName,
                contextMessage,
                contextType,
            },
        });
    };

    const handleModuleClick = (moduleName) => {
        setSelectedModule(selectedModule === moduleName ? null : moduleName);
    };

    const handleTogglePin = (tableName) => {
        setPinnedTables(prev => {
            if (prev.includes(tableName)) {
                return prev.filter(t => t !== tableName);
            } else {
                return [...prev, tableName];
            }
        });
    };

    const handleJumpToTable = (tableName) => {
        const table = tablesData?.tables?.find(t => t.tableName === tableName);
        if (table) {
            setSelectedTable(table);
            message.success(`Jumped to ${tableName}`);
        } else {
            message.warning(`Table ${tableName} not found`);
        }
    };

    // Check if connection is selected
    if (!activeConnection) {
        return (
            <div style={{ padding: 24 }}>
                <Alert
                    message="No Connection Selected"
                    description="Please select a database connection to explore."
                    type="warning"
                    showIcon
                />
            </div>
        );
    }

    // Initial loading state
    if (statusLoading || connectionInfoLoading) {
        return (
            <div style={{ textAlign: 'center', padding: '100px 0' }}>
                <Spin size="large" />
                <div style={{ marginTop: 16 }}>Loading database information...</div>
            </div>
        );
    }

    // Analyzing state
    if (analyzeMutation.isPending) {
        return (
            <div style={{ textAlign: 'center', padding: '100px 0' }}>
                <Spin size="large" />
                <div style={{ marginTop: 16, fontSize: 16, fontWeight: 500 }}>
                    Analyzing database schema...
                </div>
                <div style={{ marginTop: 8, color: '#999' }}>
                    {status?.hasQdrantData
                        ? '⚡ Using Qdrant embeddings for faster analysis...'
                        : '⚡ Fast overview mode - analyzing table names and relationships'}
                </div>
                <div style={{ marginTop: 8, color: '#666', fontSize: 12 }}>
                    Deep analysis will be performed on-demand when you click a table
                </div>
                <div style={{ marginTop: 16, color: '#666' }}>
                    <DatabaseOutlined style={{ fontSize: 24, marginRight: 8 }} />
                    {activeConnection.name}
                </div>
            </div>
        );
    }

    // No data - show 70-30 layout with connection info on right
    if (status && !status.hasData) {
        return (
            <Layout style={{ height: 'calc(100vh - 64px)', background: '#f5f5f5' }}>
                {/* Left 70% - Empty state with analyze prompt */}
                <Content style={{ padding: 24, overflow: 'auto' }}>
                    <div style={{
                        height: '100%',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        flexDirection: 'column'
                    }}>
                        <DatabaseOutlined style={{ fontSize: 80, color: '#d9d9d9', marginBottom: 24 }} />
                        <div style={{ fontSize: 20, fontWeight: 500, marginBottom: 8 }}>
                            Database Not Analyzed
                        </div>
                        <div style={{ color: '#999', marginBottom: 24, textAlign: 'center', maxWidth: 500 }}>
                            Click "Analyze Database" to start fast schema overview with AI-powered insights.
                            Deep analysis will be performed on-demand when you explore tables.
                        </div>
                        {status?.hasQdrantData && (
                            <Alert
                                message={
                                    <Space>
                                        <CheckCircleOutlined />
                                        <span>Qdrant embeddings detected ({status.qdrantPointCount} schemas)</span>
                                    </Space>
                                }
                                description="Analysis will be faster using existing vector embeddings"
                                type="success"
                                showIcon={false}
                                style={{ marginBottom: 24, maxWidth: 500 }}
                            />
                        )}
                        {analyzeMutation.isError && (
                            <Alert
                                message="Analysis Failed"
                                description={
                                    <div>
                                        <div style={{ marginBottom: 8 }}>
                                            {analyzeMutation.error?.response?.data?.details ||
                                                analyzeMutation.error?.message ||
                                                'An error occurred during analysis'}
                                        </div>
                                        <div style={{ fontSize: 12, color: '#666' }}>
                                            Common issues:
                                            <ul style={{ marginTop: 4, marginBottom: 0 }}>
                                                <li>Invalid connection string format</li>
                                                <li>Database server not accessible</li>
                                                <li>Insufficient permissions</li>
                                            </ul>
                                        </div>
                                    </div>
                                }
                                type="error"
                                showIcon
                                style={{ marginBottom: 24, maxWidth: 500 }}
                            />
                        )}
                    </div>
                </Content>

                {/* Right 30% - Connection info panel */}
                <Sider
                    width="30%"
                    theme="light"
                    style={{
                        borderLeft: '1px solid #f0f0f0',
                        overflow: 'auto',
                        padding: 24,
                        background: '#fff'
                    }}
                >
                    <Card
                        title={
                            <Space>
                                <DatabaseOutlined style={{ color: '#1890ff' }} />
                                <span>Connection Info</span>
                            </Space>
                        }
                        bordered={false}
                    >
                        <Descriptions column={1} size="small">
                            <Descriptions.Item label="Name">
                                <strong>{connectionInfo?.name || activeConnection.name}</strong>
                            </Descriptions.Item>
                            <Descriptions.Item label="Type">
                                {connectionInfo?.databaseType || 'SQL Server'}
                            </Descriptions.Item>
                            <Descriptions.Item label="Server">
                                {connectionInfo?.server || 'N/A'}
                            </Descriptions.Item>
                            <Descriptions.Item label="Database">
                                {connectionInfo?.database || 'N/A'}
                            </Descriptions.Item>
                            <Descriptions.Item label="Status">
                                <Badge status="success" text="Connected" />
                            </Descriptions.Item>
                        </Descriptions>

                        {status?.hasQdrantData && (
                            <Alert
                                message="Vector Embeddings Available"
                                description={`${status.qdrantPointCount} schemas indexed in Qdrant`}
                                type="info"
                                showIcon
                                icon={<CheckCircleOutlined />}
                                style={{ marginTop: 16, marginBottom: 16 }}
                            />
                        )}

                        <Button
                            type="primary"
                            size="large"
                            block
                            icon={<ThunderboltOutlined />}
                            onClick={handleAnalyze}
                            loading={analyzeMutation.isPending}
                            style={{ marginTop: 16 }}
                        >
                            Analyze Database
                        </Button>

                        <div style={{ marginTop: 12, fontSize: 12, color: '#999', textAlign: 'center' }}>
                            {status?.hasQdrantData
                                ? '⚡ Fast analysis using Qdrant (<10s)'
                                : '⚡ Fast overview mode (<10s for 500 tables)'}
                        </div>
                    </Card>
                </Sider>
            </Layout>
        );
    }

    // Data exists - show normal layout
    return (
        <div style={{ height: 'calc(100vh - 64px)', display: 'flex', flexDirection: 'column' }}>
            {/* Overview Card */}
            <div style={{ padding: 16, borderBottom: '1px solid #f0f0f0' }}>
                <DatabaseOverviewCard
                    overview={overview}
                    loading={overviewLoading}
                    error={overviewError}
                    onRefresh={handleAnalyze}
                    onViewHealth={handleViewHealth}
                    onViewChanges={handleViewChanges}
                    onExport={handleExport}
                    onViewIndexRecommendations={handleViewIndexRecommendations}
                    onViewNamingAnalysis={handleViewNamingAnalysis}
                    onModuleClick={handleModuleClick}
                    selectedModule={selectedModule}
                />
            </div>

            {/* Tabs for Tables/Graph View */}
            <Tabs
                activeKey={activeTab}
                onChange={setActiveTab}
                style={{ padding: '0 16px', marginBottom: 0 }}
                items={[
                    {
                        key: 'tables',
                        label: (
                            <span>
                                <DatabaseOutlined /> Tables
                            </span>
                        ),
                    },
                    {
                        key: 'graph',
                        label: (
                            <span>
                                <ApartmentOutlined /> ER Diagram
                            </span>
                        ),
                    },
                ]}
            />

            {/* Main Content */}
            {activeTab === 'tables' ? (
                <Layout style={{ flex: 1, background: '#fff' }}>
                    {/* Table List - Left Panel */}
                    <Sider
                        width={320}
                        theme="light"
                        style={{
                            borderRight: '1px solid #f0f0f0',
                            overflow: 'hidden',
                            display: 'flex',
                            flexDirection: 'column',
                        }}
                    >
                        {/* Semantic Search */}
                        <div style={{ padding: 16, borderBottom: '1px solid #f0f0f0' }}>
                            <SemanticSearch
                                connectionId={activeConnection?.id}
                                onTableSelect={handleTableSelect}
                            />
                        </div>

                        {/* Table List */}
                        <div style={{ flex: 1, overflow: 'hidden' }}>
                            <TableList
                                tables={tablesData?.tables}
                                loading={tablesLoading}
                                selectedTable={selectedTable}
                                onTableSelect={handleTableSelect}
                                pinnedTables={pinnedTables}
                                moduleFilter={selectedModule}
                            />
                        </div>
                    </Sider>

                    {/* Table Detail - Main Panel */}
                    <Content style={{ overflow: 'hidden' }}>
                        <TableDetail
                            table={tableDetail ? { ...tableDetail, connectionId: activeConnection?.id } : null}
                            loading={tableDetailLoading}
                            onQueryTable={handleQueryTable}
                            onJumpToTable={handleJumpToTable}
                            pinnedTables={pinnedTables}
                            onTogglePin={handleTogglePin}
                        />
                    </Content>
                </Layout>
            ) : (
                <div style={{ flex: 1, background: '#fff' }}>
                    <ERDiagramView
                        graphData={graphData}
                        loading={graphLoading}
                        onNodeClick={handleJumpToTable}
                        selectedTable={selectedTable?.tableName}
                    />
                </div>
            )}

            {/* Health Report Modal */}
            <HealthReport
                visible={healthModalVisible}
                onClose={() => setHealthModalVisible(false)}
                health={health}
                loading={healthLoading}
            />

            {/* Schema Changes Modal */}
            <SchemaChangesModal
                visible={changesModalVisible}
                onClose={() => setChangesModalVisible(false)}
                changes={schemaChanges}
                loading={changesLoading}
                onReAnalyze={handleReAnalyzeFromChanges}
            />

            {/* Export Documentation Modal */}
            <ExportDocumentationModal
                visible={exportModalVisible}
                onClose={() => setExportModalVisible(false)}
                connectionId={activeConnection?.id}
                databaseName={connectionInfo?.database || activeConnection?.name}
            />

            {/* Index Recommendations Modal */}
            <IndexRecommendationReport
                visible={indexRecommendationsVisible}
                onClose={() => setIndexRecommendationsVisible(false)}
                connectionId={activeConnection?.id}
            />

            {/* Naming Convention Analysis Modal */}
            <NamingConventionReport
                visible={namingAnalysisVisible}
                onClose={() => setNamingAnalysisVisible(false)}
                connectionId={activeConnection?.id}
            />
        </div>
    );
};

export default DbExplorer;
