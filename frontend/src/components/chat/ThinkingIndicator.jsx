import React, { useState, useEffect, useRef } from 'react';
import {
  LoadingOutlined,
  SearchOutlined,
  DatabaseOutlined,
  CodeOutlined,
  CheckCircleOutlined,
  PlayCircleOutlined,
  SyncOutlined,
  MessageOutlined,
  CloseCircleOutlined,
  BulbOutlined,
  SafetyOutlined,
  ThunderboltOutlined,
  DownOutlined,
  RobotOutlined,
} from '@ant-design/icons';

/**
 * ThinkingIndicator — Premium AI thinking UX component.
 * Inspired by ChatGPT/Claude thinking bubbles.
 * Shows collapsible thinking process with animated stage timeline.
 */

// Mappings for C# AgentStage integer values
const STAGE_KEYS = [
  'VALIDATING',        // 0
  'CLASSIFYING',       // 1
  'AGENT_THINKING',    // 2
  'AGENT_ACTION',      // 3
  'SCHEMA_RETRIEVAL',  // 4
  'SQL_GENERATION',    // 5
  'SQL_VALIDATION',    // 6
  'SQL_PREVIEW',       // 7
  'AWAITING_CONFIRM',  // 8
  'EXECUTING',         // 9
  'CORRECTING',        // 10
  'BUILDING_RESPONSE', // 11
  'COMPLETED',         // 12
  'BLOCKED',           // 13
  'ERROR',             // 14
];

const STAGE_META = {
  VALIDATING:       { label: 'Validating input',           icon: SafetyOutlined,       color: '#1677ff' }, // blue
  CLASSIFYING:      { label: 'Classifying intent',         icon: SearchOutlined,       color: '#1677ff' },
  AGENT_THINKING:   { label: 'Agent reasoning',            icon: BulbOutlined,         color: '#1677ff' },
  AGENT_ACTION:     { label: 'Agent action',               icon: SyncOutlined,         color: '#1677ff' },
  SCHEMA_RETRIEVAL: { label: 'Retrieving schema',          icon: DatabaseOutlined,     color: '#1677ff' },
  SQL_GENERATION:   { label: 'Generating SQL',             icon: ThunderboltOutlined,  color: '#722ed1' }, // purple for AI Gen
  SQL_VALIDATION:   { label: 'Validating SQL',             icon: CheckCircleOutlined,  color: '#1677ff' },
  SQL_PREVIEW:      { label: 'Generating preview',         icon: BulbOutlined,         color: '#faad14' }, // warning/yellow
  AWAITING_CONFIRM: { label: 'Awaiting confirmation',      icon: SyncOutlined,         color: '#faad14' },
  EXECUTING:        { label: 'Executing query',            icon: PlayCircleOutlined,   color: '#1677ff' },
  CORRECTING:       { label: 'Self-correcting',            icon: SyncOutlined,         color: '#faad14' }, // warning/yellow
  BUILDING_RESPONSE:{ label: 'Building response',          icon: MessageOutlined,      color: '#52c41a' }, // success/green
  COMPLETED:        { label: 'Completed',                  icon: CheckCircleOutlined,  color: '#52c41a' },
  BLOCKED:          { label: 'Blocked',                    icon: CloseCircleOutlined,  color: '#ff4d4f' }, // error/red
  ERROR:            { label: 'Error occurred',             icon: CloseCircleOutlined,  color: '#ff4d4f' },
};


const ThinkingIndicator = ({
  stages = [],
  currentStage,
  progress = 0,
  isStreaming = false,
  error = null,
  generatedSql = null,
}) => {
  const [isExpanded, setIsExpanded] = useState(true);
  const [elapsedMs, setElapsedMs] = useState(0);
  const [completedStages, setCompletedStages] = useState([]);
  const startTimeRef = useRef(Date.now());

  // Timer
  useEffect(() => {
    if (!isStreaming) return;
    startTimeRef.current = Date.now();
    const timer = setInterval(() => setElapsedMs(Date.now() - startTimeRef.current), 100);
    return () => clearInterval(timer);
  }, [isStreaming]);

  // Track completed stages
  useEffect(() => {
    if (stages.length > 0) {
      const getStageKey = (st) => !isNaN(st) ? STAGE_KEYS[parseInt(st)] : st;
      const currentStageKey = getStageKey(currentStage?.stage);
      
      const done = stages.filter(s => {
        const idx = stages.findIndex(st => getStageKey(st.stage) === currentStageKey);
        return stages.indexOf(s) < idx;
      });
      setCompletedStages(done.map(s => getStageKey(s.stage)));
    }
  }, [stages, currentStage]);

  if (!isStreaming && stages.length === 0) return null;

  const formatTime = (ms) => {
    const sec = Math.floor(ms / 1000);
    return sec < 60 ? `${sec}s` : `${Math.floor(sec / 60)}m ${sec % 60}s`;
  };

  const getStageKey = (st) => !isNaN(st) ? STAGE_KEYS[parseInt(st)] : st;
  const currentStageKey = getStageKey(currentStage?.stage);
  const currentMeta = currentStageKey ? STAGE_META[currentStageKey] : null;
  const CurrentIcon = currentMeta?.icon || BulbOutlined;

  return (
    <div style={styles.wrapper}>
      {/* Avatar */}
      <div style={styles.avatar}>
        <RobotOutlined style={{ fontSize: 18, color: '#fff' }} />
        <div style={styles.avatarPulse} />
      </div>

      {/* Thinking Bubble */}
      <div style={styles.bubble}>
        {/* Header — clickable to toggle */}
        <div style={styles.header} onClick={() => setIsExpanded(!isExpanded)}>
          <div style={styles.headerLeft}>
            <div style={styles.thinkingDots}>
              <span style={{ ...styles.dot, animationDelay: '0ms' }} />
              <span style={{ ...styles.dot, animationDelay: '200ms' }} />
              <span style={{ ...styles.dot, animationDelay: '400ms' }} />
            </div>
            <span style={styles.headerLabel}>
              {error ? 'Error occurred' : progress >= 100 ? 'Thinking complete' : 'Thinking...'}
            </span>
            <span style={styles.timer}>{formatTime(elapsedMs)}</span>
          </div>
          <DownOutlined
            style={{
              ...styles.chevron,
              transform: isExpanded ? 'rotate(180deg)' : 'rotate(0deg)',
            }}
          />
        </div>

        {/* Progress bar — always visible */}
        <div style={styles.progressTrack}>
          <div
            style={{
              ...styles.progressFill,
              width: `${Math.min(progress, 100)}%`,
              background: error
                ? '#ff4d4f'
                : 'linear-gradient(90deg, #1677ff, #52c41a)',
            }}
          />
          {!error && progress < 100 && <div style={styles.progressShimmer} />}
        </div>

        {/* Expandable content */}
        <div
          style={{
            ...styles.content,
            maxHeight: isExpanded ? 500 : 0,
            opacity: isExpanded ? 1 : 0,
            paddingTop: isExpanded ? 12 : 0,
            paddingBottom: isExpanded ? 4 : 0,
          }}
        >
          {/* Stage Timeline */}
          <div style={styles.timeline}>
            {stages.map((stage, idx) => {
              const stageKey = getStageKey(stage.stage);
              const meta = STAGE_META[stageKey] || { label: stageKey, color: '#8c8c8c' };
              const StageIcon = meta.icon || BulbOutlined;
              const isCurrent = currentStageKey === stageKey;
              const isDone =
                completedStages.includes(stageKey) ||
                stages.findIndex(s => getStageKey(s.stage) === currentStageKey) > idx;
              const isLast = idx === stages.length - 1;

              return (
                <div key={stage.stage} style={styles.timelineItem}>
                  {/* Connector line */}
                  {!isLast && (
                    <div
                      style={{
                        ...styles.timelineLine,
                        backgroundColor: isDone ? meta.color : '#e5e7eb',
                      }}
                    />
                  )}

                  {/* Icon */}
                  <div
                    style={{
                      ...styles.timelineIcon,
                      backgroundColor: isDone
                        ? meta.color
                        : isCurrent
                          ? `${meta.color}20`
                          : '#f3f4f6',
                      borderColor: isCurrent ? meta.color : 'transparent',
                      boxShadow: isCurrent ? `0 0 0 3px ${meta.color}25` : 'none',
                    }}
                  >
                    {isCurrent && !isDone ? (
                      <LoadingOutlined spin style={{ fontSize: 9, color: meta.color }} />
                    ) : isDone ? (
                      <CheckCircleOutlined style={{ fontSize: 9, color: '#fff' }} />
                    ) : (
                      <StageIcon style={{ fontSize: 9, color: '#bfbfbf' }} />
                    )}
                  </div>

                  {/* Label */}
                  <span
                    style={{
                      ...styles.timelineLabel,
                      color: isCurrent ? meta.color : isDone ? '#374151' : '#9ca3af',
                      fontWeight: isCurrent ? 600 : 400,
                    }}
                  >
                    {currentStage?.message && isCurrent
                      ? currentStage.message
                      : meta.label}
                  </span>
                </div>
              );
            })}
          </div>

          {/* Live SQL Preview */}
          {generatedSql && (
            <div style={styles.sqlPreview}>
              <div style={styles.sqlHeader}>
                <CodeOutlined style={{ fontSize: 12, color: '#8b5cf6' }} />
                <span style={styles.sqlLabel}>Generated SQL</span>
                <div style={styles.liveBadge}>
                  <span style={styles.liveDot} />
                  LIVE
                </div>
              </div>
              <pre style={styles.sqlCode}>
                {generatedSql}
                <span style={styles.cursor} />
              </pre>
            </div>
          )}

          {/* Error display */}
          {error && (
            <div style={styles.errorBox}>
              <CloseCircleOutlined style={{ color: '#ef4444', fontSize: 13 }} />
              <span style={styles.errorText}>
                {error.message || 'An error occurred during processing'}
              </span>
            </div>
          )}
        </div>
      </div>

      {/* Inline keyframes */}
      <style>{`
        @keyframes thinking-dot {
          0%, 60%, 100% { opacity: 0.3; transform: scale(0.8); }
          30% { opacity: 1; transform: scale(1.2); }
        }
        @keyframes shimmer {
          0% { transform: translateX(-100%); }
          100% { transform: translateX(200%); }
        }
        @keyframes blink-cursor {
          0%, 50% { opacity: 1; }
          51%, 100% { opacity: 0; }
        }
        @keyframes pulse-ring {
          0% { transform: scale(1); opacity: 0.4; }
          100% { transform: scale(1.8); opacity: 0; }
        }
        @keyframes live-pulse {
          0%, 100% { opacity: 1; }
          50% { opacity: 0.4; }
        }
      `}</style>
    </div>
  );
};

// ─── Styles ──────────────────────────────────────────
const styles = {
  wrapper: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: 10,
    marginBottom: 12,
    animation: 'fadeIn 0.3s ease',
  },
  avatar: {
    position: 'relative',
    width: 32,
    height: 32,
    borderRadius: '50%',
    background: 'linear-gradient(135deg, #1677ff, #0958d9)',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flexShrink: 0,
  },
  avatarPulse: {
    position: 'absolute',
    inset: -3,
    borderRadius: '50%',
    border: '2px solid rgba(22, 119, 255, 0.4)',
    animation: 'pulse-ring 2s ease-out infinite',
  },
  bubble: {
    flex: 1,
    maxWidth: '85%',
    backgroundColor: '#ffffff',
    borderRadius: 12,
    border: '1px solid #e5e7eb',
    boxShadow: '0 1px 3px rgba(0,0,0,0.06), 0 4px 12px rgba(0,0,0,0.04)',
    overflow: 'hidden',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: '10px 14px',
    cursor: 'pointer',
    userSelect: 'none',
    transition: 'background-color 0.15s',
  },
  headerLeft: {
    display: 'flex',
    alignItems: 'center',
    gap: 10,
  },
  thinkingDots: {
    display: 'flex',
    gap: 3,
  },
  dot: {
    width: 6,
    height: 6,
    borderRadius: '50%',
    backgroundColor: '#1677ff',
    animation: 'thinking-dot 1.4s infinite ease-in-out',
    display: 'inline-block',
  },
  headerLabel: {
    fontSize: 12,
    fontWeight: 600,
    color: '#1f2937',
  },
  timer: {
    fontSize: 11,
    color: '#9ca3af',
    fontFamily: 'SF Mono, Monaco, Consolas, monospace',
    padding: '2px 6px',
    backgroundColor: '#f3f4f6',
    borderRadius: 4,
  },
  chevron: {
    fontSize: 10,
    color: '#9ca3af',
    transition: 'transform 0.25s ease',
  },
  progressTrack: {
    height: 3,
    backgroundColor: '#f3f4f6',
    position: 'relative',
    overflow: 'hidden',
  },
  progressFill: {
    height: '100%',
    borderRadius: 2,
    transition: 'width 0.4s ease',
  },
  progressShimmer: {
    position: 'absolute',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    background: 'linear-gradient(90deg, transparent, rgba(255,255,255,0.6), transparent)',
    animation: 'shimmer 2s infinite',
  },
  content: {
    overflow: 'hidden',
    transition: 'max-height 0.35s ease, opacity 0.25s ease, padding 0.25s ease',
    paddingLeft: 14,
    paddingRight: 14,
  },
  timeline: {
    display: 'flex',
    flexDirection: 'column',
    gap: 0,
  },
  timelineItem: {
    display: 'flex',
    alignItems: 'center',
    gap: 8,
    position: 'relative',
    paddingBottom: 4,
    minHeight: 24,
  },
  timelineLine: {
    position: 'absolute',
    left: 8,
    top: 20,
    bottom: -2,
    width: 2,
    borderRadius: 1,
    transition: 'background-color 0.3s',
  },
  timelineIcon: {
    width: 18,
    height: 18,
    borderRadius: '50%',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flexShrink: 0,
    border: '2px solid transparent',
    transition: 'all 0.3s ease',
    zIndex: 1,
  },
  timelineLabel: {
    fontSize: 11,
    transition: 'color 0.2s',
    lineHeight: 1.2,
  },
  sqlPreview: {
    marginTop: 8,
    marginBottom: 4,
    borderRadius: 8,
    overflow: 'hidden',
    border: '1px solid #e5e7eb',
    backgroundColor: '#1e1e2e',
  },
  sqlHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: 6,
    padding: '6px 10px',
    backgroundColor: '#2d2d3f',
    borderBottom: '1px solid #3d3d4f',
  },
  sqlLabel: {
    fontSize: 11,
    color: '#a1a1aa',
    fontWeight: 500,
    flex: 1,
  },
  liveBadge: {
    display: 'flex',
    alignItems: 'center',
    gap: 4,
    fontSize: 9,
    fontWeight: 700,
    color: '#ef4444',
    letterSpacing: 0.5,
  },
  liveDot: {
    width: 5,
    height: 5,
    borderRadius: '50%',
    backgroundColor: '#ef4444',
    animation: 'live-pulse 1.5s infinite',
  },
  sqlCode: {
    margin: 0,
    padding: '10px 12px',
    fontSize: 12,
    lineHeight: 1.6,
    color: '#e2e8f0',
    fontFamily: 'SF Mono, Monaco, Consolas, monospace',
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-all',
    maxHeight: 120,
    overflow: 'auto',
  },
  cursor: {
    display: 'inline-block',
    width: 7,
    height: 14,
    backgroundColor: '#8b5cf6',
    animation: 'blink-cursor 1s infinite',
    verticalAlign: 'text-bottom',
    marginLeft: 2,
    borderRadius: 1,
  },
  errorBox: {
    display: 'flex',
    alignItems: 'center',
    gap: 8,
    padding: '8px 12px',
    backgroundColor: '#fef2f2',
    borderRadius: 6,
    border: '1px solid #fecaca',
    marginTop: 8,
    marginBottom: 4,
  },
  errorText: {
    fontSize: 12,
    color: '#991b1b',
  },
};

export default ThinkingIndicator;
