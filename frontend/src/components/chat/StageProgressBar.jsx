import React from 'react';
import { LoadingOutlined, SearchOutlined, DatabaseOutlined, CodeOutlined, CheckCircleOutlined, PlayCircleOutlined, SyncOutlined, MessageOutlined, CloseCircleOutlined, BulbOutlined, SettingOutlined, SafetyOutlined, ThunderboltOutlined } from '@ant-design/icons';

/**
 * LARGE-1c: Real-time stage progress bar for SSE streaming.
 * Shows pipeline stages as a horizontal progress bar with stage labels.
 * Replaces the simulated spinner during agent query processing.
 */

const ICONS = {
  loading: LoadingOutlined,
  search: SearchOutlined,
  database: DatabaseOutlined,
  code: CodeOutlined,
  'check-circle': CheckCircleOutlined,
  'play-circle': PlayCircleOutlined,
  sync: SyncOutlined,
  message: MessageOutlined,
  'close-circle': CloseCircleOutlined,
  bulb: BulbOutlined,
  setting: SettingOutlined,
  safety: SafetyOutlined,
  thunderbolt: ThunderboltOutlined,
};

const STAGE_CONFIG = {
  VALIDATING: { label: 'Validating', icon: 'safety', color: '#1890ff' },
  CLASSIFYING: { label: 'Classifying', icon: 'search', color: '#1890ff' },
  SCHEMA_RETRIEVAL: { label: 'Schema', icon: 'database', color: '#1890ff' },
  SQL_GENERATION: { label: 'Generating SQL', icon: 'thunderbolt', color: '#722ed1' },
  SQL_VALIDATION: { label: 'Validating SQL', icon: 'check-circle', color: '#1890ff' },
  EXECUTING: { label: 'Executing', icon: 'play-circle', color: '#1890ff' },
  CORRECTING: { label: 'Correcting', icon: 'sync', color: '#faad14' },
  BUILDING_RESPONSE: { label: 'Building', icon: 'message', color: '#52c41a' },
  COMPLETED: { label: 'Complete', icon: 'check-circle', color: '#52c41a' },
  ERROR: { label: 'Error', icon: 'close-circle', color: '#ff4d4f' },
  AGENT_THINKING: { label: 'Thinking', icon: 'bulb', color: '#1890ff' },
};

const StageProgressBar = ({ stages = [], currentStage, progress = 0, isStreaming = false, error = null }) => {
  if (!isStreaming && stages.length === 0) return null;

  const stageConfig = currentStage?.stage ? STAGE_CONFIG[currentStage.stage] : null;
  const IconComponent = stageConfig?.icon ? ICONS[stageConfig.icon] : null;

  return (
    <div style={styles.container}>
      {/* Progress bar */}
      <div style={styles.progressBarTrack}>
        <div
          style={{
            ...styles.progressBarFill,
            width: `${Math.min(progress, 100)}%`,
            backgroundColor: error ? '#ff4d4f' : (stageConfig?.color || '#1890ff'),
          }}
        />
      </div>

      {/* Current stage info */}
      {currentStage && (
        <div style={styles.stageInfo}>
          {IconComponent && <IconComponent style={{ color: stageConfig?.color || '#1890ff' }} />}
          <span style={styles.stageLabel}>{currentStage.message || stageConfig?.label}</span>
          <span style={styles.progressText}>{progress}%</span>
        </div>
      )}

      {/* Error display */}
      {error && (
        <div style={styles.error}>
          <CloseCircleOutlined /> {error.message || 'An error occurred during processing'}
        </div>
      )}

      {/* Stage dots */}
      <div style={styles.stageDots}>
        {stages.map((stage, idx) => {
          const config = STAGE_CONFIG[stage.stage];
          const stageIcon = config?.icon ? ICONS[config.icon] : null;
          const isActive = currentStage?.stage === stage.stage;
          const isDone = stage.progress >= 1.0 || stages.findIndex(s => s.stage === currentStage?.stage) > idx;

          return (
            <div
              key={stage.stage}
              style={{
                ...styles.dot,
                backgroundColor: isDone ? (config?.color || '#52c41a') : isActive ? (config?.color || '#1890ff') : '#d9d9d9',
                opacity: isActive ? 1 : isDone ? 0.8 : 0.4,
                transform: isActive ? 'scale(1.3)' : 'scale(1)',
              }}
              title={config?.label || stage.stage}
            >
              {stageIcon && <stageIcon style={{ fontSize: 6, color: '#fff' }} />}
            </div>
          );
        })}
      </div>
    </div>
  );
};

const styles = {
  container: {
    padding: '12px 16px',
    backgroundColor: '#fafafa',
    borderRadius: '8px',
    border: '1px solid #f0f0f0',
    marginBottom: '8px',
  },
  progressBarTrack: {
    height: '4px',
    backgroundColor: '#f0f0f0',
    borderRadius: '2px',
    overflow: 'hidden',
    marginBottom: '8px',
  },
  progressBarFill: {
    height: '100%',
    borderRadius: '2px',
    transition: 'width 0.3s ease, background-color 0.3s ease',
  },
  stageInfo: {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    fontSize: '13px',
    color: '#595959',
  },
  stageLabel: {
    flex: 1,
    fontWeight: 500,
  },
  progressText: {
    color: '#8c8c8c',
    fontSize: '12px',
    fontFamily: 'monospace',
  },
  error: {
    color: '#ff4d4f',
    fontSize: '12px',
    marginTop: '4px',
  },
  stageDots: {
    display: 'flex',
    gap: '6px',
    marginTop: '8px',
    justifyContent: 'center',
  },
  dot: {
    width: '8px',
    height: '8px',
    borderRadius: '50%',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    transition: 'all 0.3s ease',
  },
};

export default StageProgressBar;
