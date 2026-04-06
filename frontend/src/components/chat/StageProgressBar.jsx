import React from 'react';

/**
 * LARGE-1c: Real-time stage progress bar for SSE streaming.
 * Shows pipeline stages as a horizontal progress bar with stage labels.
 * Replaces the simulated spinner during agent query processing.
 */

const STAGE_CONFIG = {
  VALIDATING: { label: 'Validating', icon: '✓', color: '#8b5cf6' },
  CLASSIFYING: { label: 'Classifying', icon: '🎯', color: '#6366f1' },
  SCHEMA_RETRIEVAL: { label: 'Schema', icon: '📊', color: '#3b82f6' },
  SQL_GENERATION: { label: 'Generating SQL', icon: '⚡', color: '#0ea5e9' },
  SQL_VALIDATION: { label: 'Validating SQL', icon: '🛡️', color: '#06b6d4' },
  EXECUTING: { label: 'Executing', icon: '🚀', color: '#14b8a6' },
  CORRECTING: { label: 'Correcting', icon: '🔄', color: '#f59e0b' },
  BUILDING_RESPONSE: { label: 'Building', icon: '📦', color: '#10b981' },
  COMPLETED: { label: 'Complete', icon: '✅', color: '#22c55e' },
  ERROR: { label: 'Error', icon: '❌', color: '#ef4444' },
};

const StageProgressBar = ({ stages = [], currentStage, progress = 0, isStreaming = false, error = null }) => {
  if (!isStreaming && stages.length === 0) return null;

  const stageConfig = currentStage?.stage ? STAGE_CONFIG[currentStage.stage] : null;

  return (
    <div style={styles.container}>
      {/* Progress bar */}
      <div style={styles.progressBarTrack}>
        <div
          style={{
            ...styles.progressBarFill,
            width: `${Math.min(progress, 100)}%`,
            backgroundColor: error ? '#ef4444' : (stageConfig?.color || '#3b82f6'),
          }}
        />
      </div>

      {/* Current stage info */}
      {currentStage && (
        <div style={styles.stageInfo}>
          <span style={styles.stageIcon}>{stageConfig?.icon || '⏳'}</span>
          <span style={styles.stageLabel}>{currentStage.message || stageConfig?.label}</span>
          <span style={styles.progressText}>{progress}%</span>
        </div>
      )}

      {/* Error display */}
      {error && (
        <div style={styles.error}>
          ❌ {error.message || 'An error occurred during processing'}
        </div>
      )}

      {/* Stage dots */}
      <div style={styles.stageDots}>
        {stages.map((stage, idx) => {
          const config = STAGE_CONFIG[stage.stage];
          const isActive = currentStage?.stage === stage.stage;
          const isDone = stage.progress >= 1.0 || stages.findIndex(s => s.stage === currentStage?.stage) > idx;

          return (
            <div
              key={stage.stage}
              style={{
                ...styles.dot,
                backgroundColor: isDone ? (config?.color || '#22c55e') : isActive ? (config?.color || '#3b82f6') : '#374151',
                opacity: isActive ? 1 : isDone ? 0.8 : 0.4,
                transform: isActive ? 'scale(1.3)' : 'scale(1)',
              }}
              title={config?.label || stage.stage}
            />
          );
        })}
      </div>
    </div>
  );
};

const styles = {
  container: {
    padding: '12px 16px',
    backgroundColor: 'rgba(30, 41, 59, 0.8)',
    borderRadius: '12px',
    border: '1px solid rgba(99, 102, 241, 0.2)',
    backdropFilter: 'blur(8px)',
    marginBottom: '8px',
  },
  progressBarTrack: {
    height: '4px',
    backgroundColor: 'rgba(55, 65, 81, 0.6)',
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
    color: '#e2e8f0',
  },
  stageIcon: {
    fontSize: '16px',
  },
  stageLabel: {
    flex: 1,
    fontWeight: 500,
  },
  progressText: {
    color: '#94a3b8',
    fontSize: '12px',
    fontFamily: 'monospace',
  },
  error: {
    color: '#fca5a5',
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
    transition: 'all 0.3s ease',
  },
};

export default StageProgressBar;
