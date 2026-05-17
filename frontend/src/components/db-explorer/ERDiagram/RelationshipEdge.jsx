import { memo } from 'react';
import { BaseEdge, EdgeLabelRenderer, getBezierPath } from 'reactflow';
import { Tooltip } from 'antd';

const RelationshipEdge = ({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    style = {},
    markerEnd,
    data,
}) => {
    const [edgePath, labelX, labelY] = getBezierPath({
        sourceX,
        sourceY,
        sourcePosition,
        targetX,
        targetY,
        targetPosition,
    });

    const relationshipType = data?.relationshipType || data?.type || 'N:1';
    const columnName = data?.columnName;
    const strength = data?.strength || 'moderate';
    const tooltip = `${data?.sourceTable || 'Child'}.${columnName || '?'} -> ${data?.targetTable || 'Parent'}.${data?.targetColumn || 'PK'} (${strength})`;

    return (
        <>
            <BaseEdge path={edgePath} markerEnd={markerEnd} style={style} />
            <EdgeLabelRenderer>
                <Tooltip title={tooltip}>
                    <button
                        type="button"
                        onClick={(event) => {
                            event.stopPropagation();
                            data?.onInspect?.();
                        }}
                        style={{
                            position: 'absolute',
                            transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
                            fontSize: 10,
                            fontWeight: 600,
                            background: '#fff',
                            padding: '2px 6px',
                            borderRadius: 4,
                            border: '1px solid #d9d9d9',
                            pointerEvents: 'all',
                            color: '#262626',
                            cursor: 'pointer',
                            boxShadow: '0 1px 4px rgba(0,0,0,0.08)',
                        }}
                        className="nodrag nopan"
                    >
                        <span>{relationshipType}</span>
                        {columnName && (
                            <div style={{ fontSize: 9, color: '#666', fontWeight: 400 }}>{columnName}</div>
                        )}
                    </button>
                </Tooltip>
            </EdgeLabelRenderer>
        </>
    );
};

export default memo(RelationshipEdge);
