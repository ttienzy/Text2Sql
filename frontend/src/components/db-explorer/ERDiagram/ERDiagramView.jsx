import { useCallback, useEffect, useMemo, useState } from 'react';
import ReactFlow, {
    MiniMap,
    Controls,
    Background,
    useNodesState,
    useEdgesState,
    MarkerType,
} from 'reactflow';
import dagre from 'dagre';
import {
    Alert,
    Button,
    Descriptions,
    Divider,
    Drawer,
    Dropdown,
    Empty,
    Input,
    Select,
    Space,
    Spin,
    Switch,
    Table,
    Tag,
    Tooltip,
    message,
} from 'antd';
import {
    ApartmentOutlined,
    AimOutlined,
    BranchesOutlined,
    CopyOutlined,
    DownloadOutlined,
    FilterOutlined,
    InfoCircleOutlined,
    ReloadOutlined,
    SaveOutlined,
    SearchOutlined,
    TableOutlined,
} from '@ant-design/icons';
import TableNode from './TableNode';
import RelationshipEdge from './RelationshipEdge';
import 'reactflow/dist/style.css';

const nodeTypes = {
    tableNode: TableNode,
};

const edgeTypes = {
    relationship: RelationshipEdge,
};

const ROLE_COLORS = {
    master: '#1677ff',
    transaction: '#389e0d',
    bridge: '#d48806',
    configuration: '#722ed1',
    logaudit: '#595959',
    unknown: '#8c8c8c',
};

const EDGE_STRENGTH = {
    tight: { color: '#cf1322', width: 3, dash: undefined },
    moderate: { color: '#1677ff', width: 2.5, dash: undefined },
    loose: { color: '#8c8c8c', width: 2, dash: '6 4' },
};

const CARD_WIDTH = 260;
const CARD_HEIGHT = 180;

const normalize = (value) => String(value || '').toLowerCase();

const getStorageKey = (graphData) => {
    const tableKey = (graphData?.nodes || [])
        .map(node => node.id)
        .sort()
        .join('|');
    return `db-explorer:er-layout:${tableKey}`;
};

const getLayoutedElements = (nodes, edges, direction = 'TB') => {
    const dagreGraph = new dagre.graphlib.Graph();
    dagreGraph.setDefaultEdgeLabel(() => ({}));
    dagreGraph.setGraph({ rankdir: direction, ranksep: 120, nodesep: 90 });

    nodes.forEach((node) => {
        dagreGraph.setNode(node.id, { width: CARD_WIDTH, height: CARD_HEIGHT });
    });

    edges.forEach((edge) => {
        dagreGraph.setEdge(edge.source, edge.target);
    });

    dagre.layout(dagreGraph);

    return {
        nodes: nodes.map((node) => {
            const nodeWithPosition = dagreGraph.node(node.id);
            return {
                ...node,
                position: {
                    x: nodeWithPosition.x - CARD_WIDTH / 2,
                    y: nodeWithPosition.y - CARD_HEIGHT / 2,
                },
            };
        }),
        edges,
    };
};

const buildAdjacency = (edges) => {
    const incoming = new Map();
    const outgoing = new Map();

    edges.forEach(edge => {
        if (!outgoing.has(edge.source)) outgoing.set(edge.source, new Set());
        if (!incoming.has(edge.target)) incoming.set(edge.target, new Set());
        outgoing.get(edge.source).add(edge.target);
        incoming.get(edge.target).add(edge.source);
    });

    return { incoming, outgoing };
};

const collectReachable = (startId, adjacency) => {
    const visited = new Set([startId]);
    const queue = [startId];

    while (queue.length > 0) {
        const current = queue.shift();
        const nextNodes = adjacency.get(current) || new Set();
        nextNodes.forEach(next => {
            if (!visited.has(next)) {
                visited.add(next);
                queue.push(next);
            }
        });
    }

    return visited;
};

const downloadText = (filename, content, type = 'text/plain') => {
    const blob = new Blob([content], { type });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
};

const formatRelationType = (type) => {
    const value = String(type || 'ManyToOne');
    return value
        .replace(/([a-z])([A-Z])/g, '$1:$2')
        .replace('Many', 'N')
        .replace('One', '1');
};

const buildDbml = (nodes, edges) => {
    const tables = nodes.map(node => {
        const lines = [`Table "${node.data.tableName}" {`];
        node.data.columns.forEach(column => {
            const flags = [];
            if (column.isPrimaryKey) flags.push('pk');
            if (!column.isNullable) flags.push('not null');
            const flagText = flags.length ? ` [${flags.join(', ')}]` : '';
            lines.push(`  "${column.name}" ${column.type || 'unknown'}${flagText}`);
        });
        lines.push('}');
        return lines.join('\n');
    });

    const refs = edges.map(edge =>
        `Ref: "${edge.target}"."${edge.data.targetColumn || 'Id'}" < "${edge.source}"."${edge.data.columnName || edge.data.via || 'Id'}"`
    );

    return [...tables, ...refs].join('\n\n');
};

const buildMermaid = (nodes, edges) => {
    const lines = ['erDiagram'];
    nodes.forEach(node => {
        lines.push(`  ${node.data.tableName} {`);
        node.data.columns.forEach(column => {
            const key = column.isPrimaryKey ? ' PK' : column.isForeignKey ? ' FK' : '';
            lines.push(`    ${String(column.type || 'unknown').replace(/\s+/g, '_')} ${column.name}${key}`);
        });
        lines.push('  }');
    });

    edges.forEach(edge => {
        const source = edge.source;
        const target = edge.target;
        const label = edge.data.columnName || edge.data.via || 'relates';
        lines.push(`  ${target} ||--o{ ${source} : "${label}"`);
    });

    return lines.join('\n');
};

const ERDiagramView = ({ graphData, loading, onNodeClick, selectedTable }) => {
    const [moduleFilter, setModuleFilter] = useState(null);
    const [layout, setLayout] = useState('TB');
    const [searchValue, setSearchValue] = useState();
    const [focusTable, setFocusTable] = useState(null);
    const [focusMode, setFocusMode] = useState('all');
    const [showColumns, setShowColumns] = useState(true);
    const [showMiniMap, setShowMiniMap] = useState(true);
    const [inspectorNode, setInspectorNode] = useState(null);
    const [relationshipDrawerEdge, setRelationshipDrawerEdge] = useState(null);
    const [reactFlowInstance, setReactFlowInstance] = useState(null);
    const [savedPositions, setSavedPositions] = useState({});

    const layoutStorageKey = useMemo(() => getStorageKey(graphData), [graphData]);

    useEffect(() => {
        try {
            const saved = localStorage.getItem(layoutStorageKey);
            setSavedPositions(saved ? JSON.parse(saved) : {});
        } catch {
            setSavedPositions({});
        }
    }, [layoutStorageKey]);

    const { initialNodes, initialEdges, modules, searchOptions } = useMemo(() => {
        if (!graphData) {
            return { initialNodes: [], initialEdges: [], modules: [], searchOptions: [] };
        }

        const nodes = (graphData.nodes || []).map((node) => ({
            id: node.id,
            type: 'tableNode',
            data: {
                tableName: node.label,
                role: normalize(node.role || 'unknown'),
                rowCount: node.rowCount,
                columnCount: node.columnCount,
                module: node.module,
                primaryKeys: node.primaryKeys || [],
                foreignKeys: node.foreignKeys || [],
                columns: node.columns || [],
                showColumns,
            },
            position: savedPositions[node.id] || node.position || { x: 0, y: 0 },
        }));

        const nodeById = new Map(nodes.map(node => [node.id, node]));
        const edges = (graphData.edges || []).map((edge, index) => {
            const strength = normalize(edge.strength || 'moderate');
            const edgeStyle = EDGE_STRENGTH[strength] || EDGE_STRENGTH.moderate;
            const sourceNode = nodeById.get(edge.source);
            const targetNode = nodeById.get(edge.target);
            const sourceColumn = sourceNode?.data.columns?.find(c => c.name === edge.via);
            const targetColumn = targetNode?.data.columns?.find(c => c.isPrimaryKey)?.name;

            return {
                id: edge.id || `${edge.source}-${edge.target}-${index}`,
                source: edge.source,
                target: edge.target,
                type: 'relationship',
                data: {
                    type: edge.type,
                    relationshipType: formatRelationType(edge.type),
                    strength,
                    columnName: edge.via,
                    via: edge.via,
                    sourceTable: edge.source,
                    targetTable: edge.target,
                    sourceColumn,
                    targetColumn,
                    onInspect: () => setRelationshipDrawerEdge(edge.id || `${edge.source}-${edge.target}-${index}`),
                },
                markerEnd: {
                    type: MarkerType.ArrowClosed,
                    width: 18,
                    height: 18,
                    color: edgeStyle.color,
                },
                style: {
                    strokeWidth: edgeStyle.width,
                    stroke: edgeStyle.color,
                    strokeDasharray: edgeStyle.dash,
                },
            };
        });

        const moduleNames = [...new Set(nodes.map(n => n.data.module).filter(Boolean))].sort();
        const options = nodes.flatMap(node => [
            {
                value: `table:${node.id}`,
                label: node.data.tableName,
                type: 'table',
                tableId: node.id,
            },
            ...node.data.columns.map(column => ({
                value: `column:${node.id}.${column.name}`,
                label: `${node.data.tableName}.${column.name}`,
                type: 'column',
                tableId: node.id,
                columnName: column.name,
            })),
        ]);

        return { initialNodes: nodes, initialEdges: edges, modules: moduleNames, searchOptions: options };
    }, [graphData, savedPositions, showColumns]);

    const edgeById = useMemo(() => new Map(initialEdges.map(edge => [edge.id, edge])), [initialEdges]);
    const selectedRelationship = relationshipDrawerEdge ? edgeById.get(relationshipDrawerEdge) : null;

    const { visibleNodes, visibleEdges, relatedNodeIds, highlightedEdgeIds } = useMemo(() => {
        let filteredNodes = initialNodes;
        let filteredEdges = initialEdges;

        if (moduleFilter) {
            filteredNodes = filteredNodes.filter(node => node.data.module === moduleFilter);
            const moduleNodeIds = new Set(filteredNodes.map(node => node.id));
            filteredEdges = filteredEdges.filter(edge => moduleNodeIds.has(edge.source) && moduleNodeIds.has(edge.target));
        }

        const visibleNodeIds = new Set(filteredNodes.map(node => node.id));
        const { incoming, outgoing } = buildAdjacency(filteredEdges);
        let focusNodeIds = new Set(visibleNodeIds);
        const highlightedNodes = new Set();
        const highlightedEdges = new Set();

        if (focusTable && visibleNodeIds.has(focusTable) && focusMode !== 'all') {
            if (focusMode === 'direct') {
                focusNodeIds = new Set([focusTable]);
                (incoming.get(focusTable) || new Set()).forEach(id => focusNodeIds.add(id));
                (outgoing.get(focusTable) || new Set()).forEach(id => focusNodeIds.add(id));
            } else if (focusMode === 'upstream') {
                focusNodeIds = collectReachable(focusTable, incoming);
            } else if (focusMode === 'downstream') {
                focusNodeIds = collectReachable(focusTable, outgoing);
            }

            filteredNodes = filteredNodes.filter(node => focusNodeIds.has(node.id));
            filteredEdges = filteredEdges.filter(edge => focusNodeIds.has(edge.source) && focusNodeIds.has(edge.target));
        }

        if (focusTable && visibleNodeIds.has(focusTable)) {
            highlightedNodes.add(focusTable);
            filteredEdges.forEach(edge => {
                if (edge.source === focusTable || edge.target === focusTable) {
                    highlightedEdges.add(edge.id);
                    highlightedNodes.add(edge.source);
                    highlightedNodes.add(edge.target);
                }
            });
        }

        return {
            visibleNodes: filteredNodes,
            visibleEdges: filteredEdges,
            relatedNodeIds: highlightedNodes,
            highlightedEdgeIds: highlightedEdges,
        };
    }, [initialNodes, initialEdges, moduleFilter, focusTable, focusMode]);

    const { nodes: layoutedNodes, edges: layoutedEdges } = useMemo(() => {
        const shouldUseSavedLayout = Object.keys(savedPositions).length > 0 && layout === 'manual';
        const base = shouldUseSavedLayout
            ? { nodes: visibleNodes, edges: visibleEdges }
            : getLayoutedElements(visibleNodes, visibleEdges, layout);

        return {
            nodes: base.nodes.map(node => ({
                ...node,
                data: {
                    ...node.data,
                    isFocused: node.id === focusTable,
                    isRelated: relatedNodeIds.has(node.id),
                    dimmed: focusTable && relatedNodeIds.size > 0 && !relatedNodeIds.has(node.id),
                },
            })),
            edges: base.edges.map(edge => ({
                ...edge,
                animated: highlightedEdgeIds.has(edge.id),
                style: {
                    ...edge.style,
                    opacity: focusTable && highlightedEdgeIds.size > 0 && !highlightedEdgeIds.has(edge.id) ? 0.25 : 1,
                    strokeWidth: highlightedEdgeIds.has(edge.id) ? Math.max(edge.style?.strokeWidth || 2, 3.5) : edge.style?.strokeWidth,
                },
            })),
        };
    }, [visibleNodes, visibleEdges, layout, savedPositions, focusTable, relatedNodeIds, highlightedEdgeIds]);

    const [nodes, setNodes, onNodesChange] = useNodesState(layoutedNodes);
    const [edges, setEdges, onEdgesChange] = useEdgesState(layoutedEdges);

    useEffect(() => {
        setNodes(layoutedNodes);
        setEdges(layoutedEdges);
    }, [layoutedNodes, layoutedEdges, setNodes, setEdges]);

    useEffect(() => {
        if (selectedTable) {
            setFocusTable(selectedTable);
        }
    }, [selectedTable]);

    const focusNode = useCallback((tableId, zoom = 1.1) => {
        const node = nodes.find(item => item.id === tableId);
        if (!node || !reactFlowInstance) return;

        reactFlowInstance.setCenter(
            node.position.x + CARD_WIDTH / 2,
            node.position.y + CARD_HEIGHT / 2,
            { zoom, duration: 600 }
        );
    }, [nodes, reactFlowInstance]);

    useEffect(() => {
        if (selectedTable) {
            window.setTimeout(() => focusNode(selectedTable, 1.05), 50);
        }
    }, [selectedTable, focusNode]);

    const onNodeClickHandler = useCallback(
        (event, node) => {
            setInspectorNode(node);
            setFocusTable(node.id);
            if (onNodeClick) {
                onNodeClick(node.data.tableName);
            }
        },
        [onNodeClick]
    );

    const handleSearchSelect = (value, option) => {
        setSearchValue(value);
        setFocusTable(option.tableId);
        setFocusMode('direct');
        const node = initialNodes.find(item => item.id === option.tableId);
        if (node) setInspectorNode(node);
        window.setTimeout(() => focusNode(option.tableId, option.type === 'column' ? 1.25 : 1.1), 80);
    };

    const handleSaveLayout = () => {
        const positions = Object.fromEntries(nodes.map(node => [node.id, node.position]));
        localStorage.setItem(layoutStorageKey, JSON.stringify(positions));
        setSavedPositions(positions);
        setLayout('manual');
        message.success('Diagram layout saved in this browser.');
    };

    const handleResetLayout = () => {
        localStorage.removeItem(layoutStorageKey);
        setSavedPositions({});
        setLayout('TB');
        message.success('Diagram layout reset.');
    };

    const copyJoinSql = (edge) => {
        const sourceColumn = edge.data.columnName || edge.data.via || 'Id';
        const targetColumn = edge.data.targetColumn || 'Id';
        const sql = `SELECT *\nFROM [${edge.source}] AS child\nJOIN [${edge.target}] AS parent\n  ON child.[${sourceColumn}] = parent.[${targetColumn}];`;
        navigator.clipboard.writeText(sql);
        message.success('JOIN SQL copied.');
    };

    const handleExport = ({ key }) => {
        if (key === 'json') {
            downloadText('er-diagram.json', JSON.stringify({ nodes: visibleNodes, edges: visibleEdges }, null, 2), 'application/json');
            return;
        }

        if (key === 'dbml') {
            downloadText('schema.dbml', buildDbml(visibleNodes, visibleEdges));
            return;
        }

        if (key === 'mermaid') {
            downloadText('schema.mmd', buildMermaid(visibleNodes, visibleEdges));
        }
    };

    const focusModeOptions = [
        { value: 'all', label: 'All' },
        { value: 'direct', label: 'Direct' },
        { value: 'upstream', label: 'Upstream' },
        { value: 'downstream', label: 'Downstream' },
    ];

    const relationshipColumns = [
        {
            title: 'Relationship',
            key: 'relationship',
            render: (_, record) => (
                <Space direction="vertical" size={0}>
                    <span>{record.source} {'->'} {record.target}</span>
                    <span style={{ fontSize: 12, color: '#666' }}>{record.data.columnName || '-'}</span>
                </Space>
            ),
        },
        {
            title: 'Type',
            key: 'type',
            width: 80,
            render: (_, record) => <Tag>{record.data.relationshipType}</Tag>,
        },
        {
            title: 'Strength',
            key: 'strength',
            width: 100,
            render: (_, record) => <Tag color={record.data.strength === 'loose' ? 'default' : 'blue'}>{record.data.strength}</Tag>,
        },
        {
            title: '',
            key: 'actions',
            width: 50,
            render: (_, record) => (
                <Tooltip title="Copy JOIN SQL">
                    <Button type="text" size="small" icon={<CopyOutlined />} onClick={() => copyJoinSql(record)} />
                </Tooltip>
            ),
        },
    ];

    const inspectorRelationships = useMemo(() => {
        if (!inspectorNode) return [];
        return initialEdges.filter(edge => edge.source === inspectorNode.id || edge.target === inspectorNode.id);
    }, [initialEdges, inspectorNode]);

    if (loading) {
        return (
            <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100%' }}>
                <Spin size="large" tip="Loading ER Diagram..." />
            </div>
        );
    }

    if (!graphData || initialNodes.length === 0) {
        return (
            <Empty
                description="No graph data available"
                style={{ marginTop: 100 }}
            />
        );
    }

    return (
        <div style={{ width: '100%', height: '100%', position: 'relative', background: '#fff' }}>
            <div
                style={{
                    position: 'absolute',
                    top: 12,
                    left: 12,
                    right: 12,
                    zIndex: 10,
                    background: '#fff',
                    padding: 10,
                    borderRadius: 6,
                    boxShadow: '0 2px 10px rgba(0,0,0,0.12)',
                    display: 'flex',
                    alignItems: 'center',
                    gap: 8,
                    flexWrap: 'wrap',
                }}
            >
                <Select
                    showSearch
                    allowClear
                    style={{ width: 300 }}
                    placeholder="Search table or column"
                    value={searchValue}
                    onChange={(value, option) => value ? handleSearchSelect(value, option) : setSearchValue(undefined)}
                    optionFilterProp="label"
                    suffixIcon={<SearchOutlined />}
                    options={searchOptions}
                />

                <Select
                    style={{ width: 190 }}
                    placeholder="Filter module"
                    allowClear
                    value={moduleFilter}
                    onChange={setModuleFilter}
                    suffixIcon={<FilterOutlined />}
                    options={modules.map(module => ({ value: module, label: module }))}
                />

                <Select
                    style={{ width: 130 }}
                    value={layout}
                    onChange={setLayout}
                    options={[
                        { value: 'TB', label: 'Top-down' },
                        { value: 'LR', label: 'Left-right' },
                        { value: 'manual', label: 'Saved' },
                    ]}
                />

                <Select
                    style={{ width: 135 }}
                    value={focusMode}
                    onChange={setFocusMode}
                    options={focusModeOptions}
                />

                <Space size={6}>
                    <Switch size="small" checked={showColumns} onChange={setShowColumns} />
                    <span style={{ fontSize: 12 }}>Columns</span>
                </Space>

                <Space size={6}>
                    <Switch size="small" checked={showMiniMap} onChange={setShowMiniMap} />
                    <span style={{ fontSize: 12 }}>Map</span>
                </Space>

                <Tooltip title="Fit view">
                    <Button icon={<AimOutlined />} onClick={() => reactFlowInstance?.fitView({ duration: 500, padding: 0.2 })} />
                </Tooltip>
                <Tooltip title="Save current node positions">
                    <Button icon={<SaveOutlined />} onClick={handleSaveLayout} />
                </Tooltip>
                <Tooltip title="Reset saved layout">
                    <Button icon={<ReloadOutlined />} onClick={handleResetLayout} />
                </Tooltip>
                <Dropdown
                    menu={{
                        onClick: handleExport,
                        items: [
                            { key: 'dbml', label: 'Export DBML' },
                            { key: 'mermaid', label: 'Export Mermaid' },
                            { key: 'json', label: 'Export JSON' },
                        ],
                    }}
                >
                    <Button icon={<DownloadOutlined />}>Export</Button>
                </Dropdown>

                <div style={{ marginLeft: 'auto', fontSize: 12, color: '#666' }}>
                    {visibleNodes.length}/{initialNodes.length} tables · {visibleEdges.length}/{initialEdges.length} relationships
                </div>
            </div>

            <ReactFlow
                nodes={nodes}
                edges={edges}
                onNodesChange={onNodesChange}
                onEdgesChange={onEdgesChange}
                onNodeClick={onNodeClickHandler}
                onEdgeClick={(event, edge) => setRelationshipDrawerEdge(edge.id)}
                onInit={setReactFlowInstance}
                nodeTypes={nodeTypes}
                edgeTypes={edgeTypes}
                fitView
                minZoom={0.1}
                maxZoom={2}
                defaultEdgeOptions={{ animated: false }}
            >
                <Controls />
                {showMiniMap && (
                    <MiniMap
                        nodeColor={(node) => ROLE_COLORS[node.data.role] || '#d9d9d9'}
                        pannable
                        zoomable
                        style={{ background: '#f7f7f7' }}
                    />
                )}
                <Background variant="dots" gap={14} size={1} />
            </ReactFlow>

            <Drawer
                title={inspectorNode?.data?.tableName || 'Table'}
                open={!!inspectorNode}
                onClose={() => setInspectorNode(null)}
                width={460}
            >
                {inspectorNode && (
                    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                        <Descriptions column={1} size="small" bordered>
                            <Descriptions.Item label="Role">
                                <Tag color={ROLE_COLORS[inspectorNode.data.role]}>{inspectorNode.data.role}</Tag>
                            </Descriptions.Item>
                            <Descriptions.Item label="Module">{inspectorNode.data.module || '-'}</Descriptions.Item>
                            <Descriptions.Item label="Rows">{inspectorNode.data.rowCount?.toLocaleString() || 0}</Descriptions.Item>
                            <Descriptions.Item label="Columns">{inspectorNode.data.columnCount}</Descriptions.Item>
                            <Descriptions.Item label="Primary keys">{inspectorNode.data.primaryKeys?.join(', ') || '-'}</Descriptions.Item>
                            <Descriptions.Item label="Foreign keys">{inspectorNode.data.foreignKeys?.join(', ') || '-'}</Descriptions.Item>
                        </Descriptions>

                        <Space wrap>
                            <Button icon={<AimOutlined />} onClick={() => focusNode(inspectorNode.id, 1.2)}>
                                Focus
                            </Button>
                            <Button icon={<TableOutlined />} onClick={() => onNodeClick?.(inspectorNode.data.tableName)}>
                                Open Detail
                            </Button>
                            <Button
                                icon={<BranchesOutlined />}
                                onClick={() => {
                                    setFocusTable(inspectorNode.id);
                                    setFocusMode('direct');
                                    focusNode(inspectorNode.id, 1.1);
                                }}
                            >
                                Direct Relations
                            </Button>
                        </Space>

                        <Divider orientation="left">Columns</Divider>
                        <Table
                            dataSource={inspectorNode.data.columns}
                            rowKey="name"
                            size="small"
                            pagination={{ pageSize: 8, size: 'small' }}
                            columns={[
                                {
                                    title: 'Column',
                                    dataIndex: 'name',
                                    render: (text, record) => (
                                        <Space>
                                            {record.isPrimaryKey && <Tag color="gold">PK</Tag>}
                                            {record.isForeignKey && <Tag color="blue">FK</Tag>}
                                            <span>{text}</span>
                                        </Space>
                                    ),
                                },
                                {
                                    title: 'Type',
                                    dataIndex: 'type',
                                    width: 130,
                                },
                            ]}
                        />

                        <Divider orientation="left">Relationships</Divider>
                        {inspectorRelationships.length > 0 ? (
                            <Table
                                dataSource={inspectorRelationships}
                                rowKey="id"
                                size="small"
                                pagination={false}
                                columns={relationshipColumns}
                            />
                        ) : (
                            <Alert type="info" showIcon message="No relationships found for this table." />
                        )}
                    </Space>
                )}
            </Drawer>

            <Drawer
                title="Relationship"
                open={!!selectedRelationship}
                onClose={() => setRelationshipDrawerEdge(null)}
                width={420}
            >
                {selectedRelationship && (
                    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                        <Descriptions column={1} size="small" bordered>
                            <Descriptions.Item label="From">{selectedRelationship.source}</Descriptions.Item>
                            <Descriptions.Item label="To">{selectedRelationship.target}</Descriptions.Item>
                            <Descriptions.Item label="Via">{selectedRelationship.data.columnName || '-'}</Descriptions.Item>
                            <Descriptions.Item label="Type">{selectedRelationship.data.relationshipType}</Descriptions.Item>
                            <Descriptions.Item label="Strength">{selectedRelationship.data.strength}</Descriptions.Item>
                        </Descriptions>
                        <Alert
                            type="info"
                            showIcon
                            icon={<InfoCircleOutlined />}
                            message="Nullable foreign keys are shown as loose relationships. Non-null relationships are stronger candidates for joins."
                        />
                        <Button icon={<CopyOutlined />} onClick={() => copyJoinSql(selectedRelationship)} block>
                            Copy JOIN SQL
                        </Button>
                        <Button
                            icon={<ApartmentOutlined />}
                            onClick={() => {
                                setFocusTable(selectedRelationship.source);
                                setFocusMode('direct');
                                focusNode(selectedRelationship.source, 1.1);
                            }}
                            block
                        >
                            Focus Relationship Area
                        </Button>
                    </Space>
                )}
            </Drawer>
        </div>
    );
};

export default ERDiagramView;
