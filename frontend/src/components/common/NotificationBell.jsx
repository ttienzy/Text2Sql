import React, { useState, useEffect, useCallback } from 'react';
import { Badge, Dropdown, Button, Empty } from 'antd';
import { BellOutlined, ReloadOutlined } from '@ant-design/icons';

/**
 * MED-1: NotificationBell — shows pending approval notifications.
 * 
 * ✅ FIX: Uses event-based notifications instead of polling a non-existent API.
 * Listens for 'agent:approval-needed' custom events dispatched from ChatArea
 * when a pipeline response has requiresConfirmation=true.
 */
const NotificationBell = ({ onOpenApprovalModal }) => {
  const [notifications, setNotifications] = useState([]);

  // Listen for approval-needed events from the chat pipeline
  const handleApprovalEvent = useCallback((event) => {
    const { sessionId, question, sqlPreview, clarificationType } = event.detail || {};
    setNotifications(prev => [
      {
        id: sessionId || `notif-${Date.now()}`,
        clarificationType: clarificationType || 'dml_confirmation',
        question: question || 'Operation requires approval',
        sqlPreview,
        timestamp: new Date().toISOString(),
      },
      ...prev,
    ]);
  }, []);

  useEffect(() => {
    window.addEventListener('agent:approval-needed', handleApprovalEvent);
    return () => window.removeEventListener('agent:approval-needed', handleApprovalEvent);
  }, [handleApprovalEvent]);

  const clearNotification = (id) => {
    setNotifications(prev => prev.filter(n => n.id !== id));
  };

  const clearAll = () => setNotifications([]);

  const menuItems = notifications.length === 0
    ? [
      {
        key: 'empty',
        label: (
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description="No pending approvals"
            style={{ padding: '12px 0' }}
          />
        ),
        disabled: true,
      }
    ]
    : notifications.map((req) => ({
      key: req.id,
      label: (
        <div style={{ padding: '4px 0', maxWidth: 260 }}>
          <div style={{ fontWeight: 500, marginBottom: 4 }}>
            {req.clarificationType === 'dml_confirmation' ? '✏️ Data Modification' : '🏗️ DDL Operation'}
          </div>
          <div style={{ fontSize: 12, color: '#8c8c8c', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
            {req.question?.substring(0, 60) || 'Pending approval'}
            {req.question?.length > 60 ? '...' : ''}
          </div>
          <div style={{ fontSize: 11, color: '#bfbfbf', marginTop: 2 }}>
            {req.timestamp ? new Date(req.timestamp).toLocaleTimeString() : ''}
          </div>
        </div>
      ),
      onClick: () => {
        onOpenApprovalModal?.(req);
        clearNotification(req.id);
      },
    }));

  // Add clear-all button when there are notifications
  if (notifications.length > 0) {
    menuItems.push(
      { type: 'divider' },
      {
        key: 'clear-all',
        label: (
          <Button type="text" size="small" onClick={clearAll} block>
            Clear all
          </Button>
        ),
      }
    );
  }

  return (
    <Dropdown
      menu={{ items: menuItems }}
      trigger={['click']}
      placement="bottomRight"
    >
      <Badge count={notifications.length} size="small">
        <Button
          type="text"
          icon={<BellOutlined style={{ fontSize: 18 }} />}
          style={{ padding: '4px 8px' }}
        />
      </Badge>
    </Dropdown>
  );
};

export default NotificationBell;
