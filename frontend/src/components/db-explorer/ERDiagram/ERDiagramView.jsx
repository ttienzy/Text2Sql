import { useCallback, useMemo, useState, useEffect } from 'react';
import ReactFlow, {
    MiniMap,
    Controls,
    Background,
    useNodesState,
    useEdgesState,
    MarkerType,
} from 'reactflow';
import dagre from 'dagre';
import { Button, Space, Select, Spin, Empty, message } from 'antd';
import { FullscreenOutlined, DownloadOutlined, FilterOutlined } from '@ant-design/icons';
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
    master: '#1890ff',
    transaction: '#52c41a',
    bridge: '#faad14',
    configuration: '#722ed1',
    logaudit: '#8c8c8c',
    unknown: '#d9d9d9',
};

// Auto-layout using dagre
const getLayoutedElements = (nodes, edges, direction = 'TB') => {
    const dagreGraph = new dagre.graphlib.Graph();
    dagreGraph.setDefaultEdgeLabel(() => ({}));
    dagreGraph.setGraph({ rankdir: direction, ranksep: 100, nodesep: 80 });

    nodes.forEach((node) => {
        dagreGraph.setNode(node.id, { width: 220, height: 150 });
    });

    edges.forEach((edge) => {
        dagreGraph.setEdge(edge.source, edge.target);
    });

    dagre.layout(dagreGraph);

    const layoutedNodes = nodes.map((node) => {
        const nodeWithPosition = dagreGraph.node(node.id);
        return {
            ...node,
            position: {
                x: nodeWithPosition.x - 110,
                y: nodeWithPosition.y - 75,
            },
        };
    });

    return { nodes: layoutedNodes, edges };
};

const ERDiagramView = ({ graphData, loading, onNodeClick, selectedTable }) => {
    const [moduleFilter, setModuleFilter] = useState(null);
    const [layout, setLayout] = useState('TB'); // TB = top-bottom, LR = left-right

    // Convert graph data to React Flow format
    const { initialNodes, initialEdges, modules } = useMemo(() => {
        if (!graphData) return { initialNodes: [], initialEdges: [], modules: [] };

        const nodes = graphData.nodes.map((node) => ({
            id: node.id,
            type: 'tableNode',
            data: {
                tableName: node.label,
                role: node.role,
                rowCount: node.rowCount,
                columnCount: node.columnCount,
                module: node.module,
                primaryKeys: node.primaryKeys,
                foreignKeys: node.foreignKeys,
                columns: node.columns || [],
            },
            position: node.position || { x: 0, y: 0 },
        }));

        const edges = graphData.edges.map((edge, index) => ({
            id: `${edge.source}-${edge.target}-${index}`,
            source: edge.source,
            target: edge.target,
            type: 'relationship',
            data: {
                type: edge.type,
                columnName: edge.label,
            },
            markerEnd: {
                type: MarkerType.ArrowClosed,
                width: 20,
                height: 20,
            },
            style: {
                strokeWidth: 2,
                stroke: '#999',
            },
        }));

        const uniqueModules = [...new Set(nodes.map(n => n.data.module).filter(Boolean))];

        return { initialNodes: nodes, initialEdges: edges, modules: uniqueModules };
    }, [graphData]);

    // Apply layout
    const { nodes: layoutedNodes, edges: layoutedEdges } = useMemo(() => {
        let filteredNodes = initialNodes;
        let filteredEdges = initialEdges;

        // Filter by module
        if (moduleFilter) {
            filteredNodes = initialNodes.filter(n => n.data.module === moduleFilter);
            const nodeIds = new Set(filteredNodes.map(n => n.id));
            filteredEdges = initialEdges.filter(e => nodeIds.has(e.source) && nodeIds.has(e.target));
        }

        return getLayoutedElements(filteredNodes, filteredEdges, layout);
    }, [initialNodes, initialEdges, moduleFilter, layout]);

    const [nodes, setNodes, onNodesChange] = useNodesState(layoutedNodes);
    const [edges, setEdges, onEdgesChange] = useEdgesState(layoutedEdges);

    // Update nodes when layout changes
    useEffect(() => {
        setNodes(layoutedNodes);
        setEdges(layoutedEdges);
    }, [layoutedNodes, layoutedEdges, setNodes, setEdges]);

    // Highlight selected table
    useEffect(() => {
        if (selectedTable) {
            setNodes((nds) =>
                nds.map((node) => ({
                    ...node,
                    selected: node.data.tableName === selectedTable,
                }))
            );
        }
    }, [selectedTable, setNodes]);

    const onNodeClickHandler = useCallback(
        (event, node) => {
            if (onNodeClick) {
                onNodeClick(node.data.tableName);
            }
        },
        [onNodeClick]
    );

    const handleExportPNG = () => {
        message.info('Export PNG feature coming soon!');
        // TODO: Implement with html2canvas
    };

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
        <div style={{ width: '100%', height: '100%', position: 'relative' }}>
            {/* Controls */}
            <div
                style={{
                    position: 'absolute',
                    top: 16,
                    left: 16,
                    zIndex: 10,
                    background: '#fff',
                    padding: 12,
                    borderRadius: 8,
                    boxShadow: '0 2px 8px rgba(0,0,0,0.1)',
                }}
            >
                <Space direction="vertical" size="small">
                    <Select
                        style={{ width: 200 }}
                        placeholder="Filter by module"
                        allowClear
                        value={moduleFilter}
                        onChange={setModuleFilter}
                        suffixIcon={<FilterOutlined />}
                    >
                        {modules.map((module) => (
                            <Select.Option key={module} value={module}>
                                📦 {module}
                            </Select.Option>
                        ))}
                    </Select>

                    <Select
                        style={{ width: 200 }}
                        value={layout}
                        onChange={setLayout}
                    >
                        <Select.Option value="TB">Top to Bottom</Select.Option>
                        <Select.Option value="LR">Left to Right</Select.Option>
                    </Select>

                    <Button
                        icon={<DownloadOutlined />}
                        onClick={handleExportPNG}
                        block
                    >
                        Export PNG
                    </Button>
                </Space>
            </div>

            {/* React Flow */}
            <ReactFlow
                nodes={nodes}
                edges={edges}
                onNodesChange={onNodesChange}
                onEdgesChange={onEdgesChange}
                onNodeClick={onNodeClickHandler}
                nodeTypes={nodeTypes}
                edgeTypes={edgeTypes}
                fitView
                minZoom={0.1}
                maxZoom={2}
                defaultEdgeOptions={{
                    animated: false,
                }}
            >
                <Controls />
                <MiniMap
                    nodeColor={(node) => ROLE_COLORS[node.data.role] || '#d9d9d9'}
                    style={{
                        background: '#f5f5f5',
                    }}
                />
                <Background variant="dots" gap={12} size={1} />
            </ReactFlow>
        </div>
    );
};

export default ERDiagramView;
