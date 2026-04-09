import React, { useState } from 'react';

/**
 * MED-5: ErrorRecovery — displays actionable error recovery options
 * when a query fails. Offers alternative phrasing suggestions,
 * retry with different wording, and error reporting.
 * 
 * ✅ P0: Added support for actionButton from error object (e.g., Test Connection)
 */
const ErrorRecovery = ({ error, originalQuestion, suggestedQueries, onRetry, onReport, actionButton }) => {
  const [altQuestion, setAltQuestion] = useState('');
  const [reported, setReported] = useState(false);
  const [showReport, setShowReport] = useState(false);
  const [reportContext, setReportContext] = useState('');

  if (!error) return null;

  const handleRetry = () => {
    if (altQuestion.trim()) {
      onRetry?.(altQuestion.trim());
      setAltQuestion('');
    }
  };

  const handleReport = async () => {
    await onReport?.({
      originalQuestion,
      error: typeof error === 'string' ? error : error?.message,
      additionalContext: reportContext,
    });
    setReported(true);
    setTimeout(() => setReported(false), 3000);
  };

  return (
    <div style={styles.container}>
      {/* Error Display */}
      <div style={styles.errorBox}>
        <span style={styles.errorIcon}>❌</span>
        <div style={styles.errorContent}>
          <div style={styles.errorTitle}>Query Failed</div>
          <div style={styles.errorMessage}>
            {typeof error === 'string' ? error : error?.message || 'An unknown error occurred'}
          </div>
        </div>
      </div>

      {/* ✅ P0: Action Button (e.g., Test Connection) */}
      {actionButton && (
        <div style={styles.section}>
          <button
            style={styles.actionBtn}
            onClick={actionButton.action}
          >
            {actionButton.label}
          </button>
        </div>
      )}

      {/* Suggested Alternatives */}
      {suggestedQueries?.length > 0 && (
        <div style={styles.section}>
          <div style={styles.sectionTitle}>💡 Try these alternatives:</div>
          <div style={styles.suggestions}>
            {suggestedQueries.map((q, idx) => (
              <button
                key={idx}
                style={styles.suggestionBtn}
                onClick={() => onRetry?.(q)}
              >
                {q}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Retry with different wording */}
      <div style={styles.section}>
        <div style={styles.sectionTitle}>🔄 Retry with different wording:</div>
        <div style={styles.retryRow}>
          <input
            type="text"
            value={altQuestion}
            onChange={e => setAltQuestion(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && handleRetry()}
            placeholder="Try rephrasing your question..."
            style={styles.retryInput}
          />
          <button
            style={styles.retryBtn}
            onClick={handleRetry}
            disabled={!altQuestion.trim()}
          >
            Retry
          </button>
        </div>
      </div>

      {/* Report Error */}
      <div style={styles.section}>
        {!showReport ? (
          <button style={styles.reportToggle} onClick={() => setShowReport(true)}>
            🐛 Report this error
          </button>
        ) : (
          <div style={styles.reportSection}>
            <textarea
              value={reportContext}
              onChange={e => setReportContext(e.target.value)}
              placeholder="Additional context (optional)..."
              style={styles.reportTextarea}
              rows={3}
            />
            <button
              style={styles.reportBtn}
              onClick={handleReport}
              disabled={reported}
            >
              {reported ? '✅ Reported!' : '📤 Send Report'}
            </button>
          </div>
        )}
      </div>
    </div>
  );
};

const styles = {
  container: {
    backgroundColor: 'rgba(239,68,68,0.05)',
    borderRadius: '12px', border: '1px solid rgba(239,68,68,0.2)',
    padding: '16px', marginTop: '8px',
  },
  errorBox: {
    display: 'flex', alignItems: 'flex-start', gap: '10px', marginBottom: '16px',
  },
  errorIcon: { fontSize: '20px', flexShrink: 0 },
  errorContent: { flex: 1 },
  errorTitle: { color: '#fca5a5', fontWeight: 600, fontSize: '14px', marginBottom: '4px' },
  errorMessage: { color: '#e2e8f0', fontSize: '13px', lineHeight: 1.5 },
  section: { marginBottom: '12px' },
  sectionTitle: { color: '#94a3b8', fontSize: '12px', fontWeight: 600, marginBottom: '8px' },
  // ✅ P0: Action button style (primary CTA)
  actionBtn: {
    padding: '10px 20px', borderRadius: '8px', border: 'none',
    backgroundColor: '#1890ff', color: '#fff', fontSize: '14px',
    fontWeight: 600, cursor: 'pointer', width: '100%',
    transition: 'background-color 0.15s',
  },
  suggestions: { display: 'flex', flexDirection: 'column', gap: '6px' },
  suggestionBtn: {
    padding: '8px 12px', borderRadius: '8px', border: '1px solid rgba(99,102,241,0.2)',
    backgroundColor: 'rgba(99,102,241,0.08)', color: '#a5b4fc',
    fontSize: '13px', cursor: 'pointer', textAlign: 'left',
    transition: 'background-color 0.15s',
  },
  retryRow: { display: 'flex', gap: '8px' },
  retryInput: {
    flex: 1, padding: '8px 12px', borderRadius: '8px',
    border: '1px solid #334155', backgroundColor: '#0f172a',
    color: '#e2e8f0', fontSize: '13px',
  },
  retryBtn: {
    padding: '8px 16px', borderRadius: '8px', border: 'none',
    backgroundColor: '#4f46e5', color: '#fff', fontSize: '13px',
    fontWeight: 600, cursor: 'pointer', whiteSpace: 'nowrap',
  },
  reportToggle: {
    background: 'none', border: 'none', color: '#94a3b8',
    fontSize: '12px', cursor: 'pointer', padding: 0,
  },
  reportSection: { display: 'flex', flexDirection: 'column', gap: '8px' },
  reportTextarea: {
    padding: '8px 12px', borderRadius: '8px', border: '1px solid #334155',
    backgroundColor: '#0f172a', color: '#e2e8f0', fontSize: '13px',
    resize: 'vertical', fontFamily: 'inherit',
  },
  reportBtn: {
    padding: '6px 16px', borderRadius: '8px', border: 'none',
    backgroundColor: 'rgba(245,158,11,0.15)', color: '#fbbf24',
    fontSize: '12px', fontWeight: 600, cursor: 'pointer', alignSelf: 'flex-start',
  },
};

export default ErrorRecovery;
