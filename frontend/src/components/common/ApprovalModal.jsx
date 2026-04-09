import React, { useState } from 'react';

/**
 * MED-1: ApprovalModal — displays DML/DDL operation previews
 * requiring user approval before execution.
 * Shows SQL preview, affected tables, and approve/reject controls.
 */
const ApprovalModal = ({ visible, request, onApprove, onReject, onClose }) => {
  const [rejectReason, setRejectReason] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  if (!visible || !request) return null;

  const handleApprove = async () => {
    setIsSubmitting(true);
    try {
      await onApprove?.(request.sessionId);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleReject = async () => {
    setIsSubmitting(true);
    try {
      await onReject?.(request.sessionId, rejectReason || 'User rejected');
    } finally {
      setIsSubmitting(false);
      setRejectReason('');
    }
  };

  return (
    <div style={styles.overlay} onClick={onClose}>
      <div style={styles.modal} onClick={e => e.stopPropagation()}>
        {/* Header */}
        <div style={styles.header}>
          <span style={styles.headerIcon}>⚠️</span>
          <h3 style={styles.headerTitle}>Operation Requires Approval</h3>
          <button style={styles.closeBtn} onClick={onClose}>×</button>
        </div>

        {/* Content */}
        <div style={styles.content}>
          {/* Operation type badge */}
          <div style={styles.badge}>
            {request.operationType === 'DDL' ? '🏗️ DDL Operation' : '✏️ Data Modification'}
          </div>

          {/* Description */}
          <p style={styles.description}>{request.message || 'This operation will modify the database.'}</p>

          {/* SQL Preview */}
          {request.sqlPreview && (
            <div style={styles.sqlPreview}>
              <div style={styles.sqlHeader}>SQL Preview</div>
              <pre style={styles.sqlCode}>{request.sqlPreview}</pre>
            </div>
          )}

          {/* Affected tables */}
          {request.affectedTables?.length > 0 && (
            <div style={styles.affectedSection}>
              <strong>Affected tables:</strong>
              <div style={styles.tableChips}>
                {request.affectedTables.map(t => (
                  <span key={t} style={styles.chip}>{t}</span>
                ))}
              </div>
            </div>
          )}

          {/* Auto-reject timer */}
          <p style={styles.timerNote}>
            ⏱ This request will auto-reject in 5 minutes if no action is taken.
          </p>

          {/* Reject reason input */}
          <div style={styles.rejectSection}>
            <label style={styles.rejectLabel}>Rejection reason (optional):</label>
            <input
              type="text"
              value={rejectReason}
              onChange={e => setRejectReason(e.target.value)}
              placeholder="e.g. Not needed at this time"
              style={styles.rejectInput}
            />
          </div>
        </div>

        {/* Actions */}
        <div style={styles.actions}>
          <button
            style={{ ...styles.btn, ...styles.rejectBtn }}
            onClick={handleReject}
            disabled={isSubmitting}
          >
            ❌ Reject
          </button>
          <button
            style={{ ...styles.btn, ...styles.approveBtn }}
            onClick={handleApprove}
            disabled={isSubmitting}
          >
            ✅ Approve & Execute
          </button>
        </div>
      </div>
    </div>
  );
};

const styles = {
  overlay: {
    position: 'fixed', top: 0, left: 0, right: 0, bottom: 0,
    backgroundColor: 'rgba(0,0,0,0.6)', display: 'flex',
    alignItems: 'center', justifyContent: 'center', zIndex: 1000,
    backdropFilter: 'blur(4px)',
  },
  modal: {
    backgroundColor: '#1e293b', borderRadius: '16px', width: '520px',
    maxWidth: '90vw', border: '1px solid rgba(99,102,241,0.3)',
    boxShadow: '0 25px 50px -12px rgba(0,0,0,0.5)',
  },
  header: {
    display: 'flex', alignItems: 'center', padding: '16px 20px',
    borderBottom: '1px solid rgba(255,255,255,0.1)',
  },
  headerIcon: { fontSize: '24px', marginRight: '10px' },
  headerTitle: { margin: 0, flex: 1, color: '#f1f5f9', fontSize: '16px' },
  closeBtn: {
    background: 'none', border: 'none', color: '#94a3b8', fontSize: '24px',
    cursor: 'pointer', padding: '0 4px',
  },
  content: { padding: '20px' },
  badge: {
    display: 'inline-block', padding: '4px 12px', borderRadius: '20px',
    backgroundColor: 'rgba(245,158,11,0.15)', color: '#fbbf24',
    fontSize: '12px', fontWeight: 600, marginBottom: '12px',
  },
  description: { color: '#cbd5e1', fontSize: '14px', margin: '0 0 16px' },
  sqlPreview: { marginBottom: '16px', borderRadius: '8px', overflow: 'hidden' },
  sqlHeader: {
    backgroundColor: 'rgba(99,102,241,0.15)', padding: '8px 12px',
    color: '#a5b4fc', fontSize: '12px', fontWeight: 600,
  },
  sqlCode: {
    backgroundColor: '#0f172a', padding: '12px', margin: 0,
    color: '#e2e8f0', fontSize: '12px', fontFamily: 'monospace',
    whiteSpace: 'pre-wrap', overflowX: 'auto',
  },
  affectedSection: { marginBottom: '12px', color: '#94a3b8', fontSize: '13px' },
  tableChips: { display: 'flex', flexWrap: 'wrap', gap: '6px', marginTop: '6px' },
  chip: {
    padding: '2px 10px', borderRadius: '12px', fontSize: '12px',
    backgroundColor: 'rgba(59,130,246,0.15)', color: '#93c5fd',
  },
  timerNote: { color: '#f59e0b', fontSize: '12px', fontStyle: 'italic' },
  rejectSection: { marginTop: '12px' },
  rejectLabel: { color: '#94a3b8', fontSize: '12px', display: 'block', marginBottom: '4px' },
  rejectInput: {
    width: '100%', padding: '8px 12px', borderRadius: '8px', border: '1px solid #334155',
    backgroundColor: '#0f172a', color: '#e2e8f0', fontSize: '13px',
    outline: 'none', boxSizing: 'border-box',
  },
  actions: {
    display: 'flex', justifyContent: 'flex-end', gap: '10px',
    padding: '16px 20px', borderTop: '1px solid rgba(255,255,255,0.1)',
  },
  btn: {
    padding: '8px 20px', borderRadius: '8px', border: 'none',
    fontSize: '13px', fontWeight: 600, cursor: 'pointer',
  },
  rejectBtn: { backgroundColor: 'rgba(239,68,68,0.15)', color: '#fca5a5' },
  approveBtn: { backgroundColor: 'rgba(34,197,94,0.2)', color: '#86efac' },
};

export default ApprovalModal;
