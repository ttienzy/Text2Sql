import { Tooltip, Tag, Space } from 'antd';
import { ThunderboltOutlined, DatabaseOutlined, ClockCircleOutlined, CheckCircleOutlined, SyncOutlined } from '@ant-design/icons';

const QueryTimeBadge = ({ durationMs, showIcon = true }) => {
  if (!durationMs) return null;

  let category = 'fast';
  let label = `${durationMs}ms`;

  if (durationMs < 500) {
    category = 'fast';
  } else if (durationMs < 2000) {
    category = 'medium';
    label = `${(durationMs / 1000).toFixed(1)}s`;
  } else {
    category = 'slow';
    label = `${(durationMs / 1000).toFixed(1)}s`;
  }

  return (
    <span className={`query-time-badge ${category}`}>
      {showIcon && <ThunderboltOutlined />}
      {label}
    </span>
  );
};

const CacheIndicator = ({ cached, size }) => {
  const icon = cached ? <CheckCircleOutlined /> : <SyncOutlined />;
  const label = cached ? 'Cached' : 'Fresh';

  return (
    <Tooltip title={cached ? 'Results loaded from cache' : 'Query executed fresh'}>
      <span className={`cache-indicator ${cached ? 'cached' : 'uncached'}`}>
        {icon}
        {size !== false && <span>{label}</span>}
      </span>
    </Tooltip>
  );
};

const ConnectionLatency = ({ latencyMs }) => {
  if (!latencyMs && latencyMs !== 0) return null;

  let status = 'good';
  if (latencyMs > 200) status = 'slow';

  return (
    <Tooltip title={`Database latency: ${latencyMs}ms`}>
      <span className="connection-latency">
        <DatabaseOutlined />
        <span>{latencyMs}ms</span>
      </span>
    </Tooltip>
  );
};

const ExecutionStats = ({ executionTimeMs, isCached, rowCount, latencyMs }) => {
  if (!executionTimeMs && !isCached && !rowCount) return null;

  return (
    <Space size="small" wrap>
      {executionTimeMs > 0 && <QueryTimeBadge durationMs={executionTimeMs} />}
      {isCached !== undefined && <CacheIndicator cached={isCached} />}
      {latencyMs > 0 && <ConnectionLatency latencyMs={latencyMs} />}
      {rowCount !== undefined && (
        <Tag color="blue">
          {rowCount} {rowCount === 1 ? 'row' : 'rows'}
        </Tag>
      )}
    </Space>
  );
};

export { QueryTimeBadge, CacheIndicator, ConnectionLatency, ExecutionStats };
export default ExecutionStats;