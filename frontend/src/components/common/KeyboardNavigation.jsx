import { useEffect, useCallback } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { message } from 'antd';

const KeyboardNavigation = () => {
  const navigate = useNavigate();
  const location = useLocation();

  const handleKeyDown = useCallback((e) => {
    // Ignore if typing in input/textarea
    const target = e.target;
    const isInput = target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable;
    const isCtrl = e.ctrlKey || e.metaKey;

    // Ctrl/Cmd + K: Command palette / Search
    if (isCtrl && e.key === 'k') {
      e.preventDefault();
      message.info('Search (Ctrl+K) - Coming soon');
      return;
    }

    // Ctrl/Cmd + 1-5: Navigate to pages
    if (isCtrl && !isInput) {
      const routes = {
        '1': '/chat',
        '2': '/explorer', 
        '3': '/query-lab',
        '4': '/connections',
        '5': '/settings',
      };

      if (routes[e.key]) {
        e.preventDefault();
        navigate(routes[e.key]);
        return;
      }
    }

    // Escape: Close modals or go back
    if (e.key === 'Escape') {
      // Could dispatch close event for modals
      // Currently handled by antd modals automatically
    }

    // Ctrl/Cmd + N: New connection
    if (isCtrl && e.key.toLowerCase() === 'n' && location.pathname === '/connections') {
      e.preventDefault();
      // Trigger new connection modal - could be enhanced
      return;
    }

    // Ctrl/Cmd + L: Clear/Focus search
    if (isCtrl && e.key.toLowerCase() === 'l') {
      e.preventDefault();
      const searchInput = document.querySelector('[placeholder*="Search"], input[type="search"]');
      searchInput?.focus();
      return;
    }

    // Ctrl/Cmd + /: Show shortcuts help
    if (isCtrl && e.key === '/') {
      e.preventDefault();
      message.info(
        <div>
          <strong>Keyboard Shortcuts</strong>
          <div style={{ marginTop: 8, fontSize: 12 }}>
            <div>Ctrl+1: Chat</div>
            <div>Ctrl+2: Explorer</div>
            <div>Ctrl+3: Query Lab</div>
            <div>Ctrl+4: Connections</div>
            <div>Ctrl+5: Settings</div>
            <div>Ctrl+K: Search</div>
            <div>Ctrl+L: Focus search</div>
            <div>Ctrl+Enter: Send message</div>
            <div>Escape: Close modal</div>
          </div>
        </div>,
        5
      );
      return;
    }

  }, [navigate, location]);

  useEffect(() => {
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [handleKeyDown]);

  return null;
};

export default KeyboardNavigation;