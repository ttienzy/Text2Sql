import { memo } from 'react';
import { BaseEdge, EdgeLabelRenderer, getBezierPath } from 'reactflow';

const RelationshipEdge = ({
    id,
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

    const relationshipType = data?.type || '1:N';
    const columnName = data?.columnName;

    return (
        <>
            <BaseEdge path={edgePath} markerEnd={markerEnd} style={style} />
            <EdgeLabelRenderer>
                <div
                    style={{
                        position: 'absolute',
                        transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
                        fontSize: 10,
                        fontWeight: 500,
                        background: '#fff',
                        padding: '2px 6px',
                        borderRadius: 4,
                        border: '1px solid #d9d9d9',
                        pointerEvents: 'all',
                    }}
                    className="nodrag nopan"
                >
                    {relationshipType}
                    {columnName && (
                        <div style={{ fontSize: 9, color: '#999' }}>{columnName}</div>
                    )}
                </div>
            </EdgeLabelRenderer>
        </>
    );
};

export default memo(RelationshipEdge);
