import React, { useState, useEffect, useCallback } from 'react';
import { Input, Button, Empty, Spin } from 'antd';
import { SearchOutlined, StarOutlined, StarFilled, ReloadOutlined } from '@ant-design/icons';
import axiosInstance from '../../api/axios';
import { API_ENDPOINTS } from '../../constants';

/**
 * MED-2: QueryHistory — displays past conversations with filtering,
 * favorites, and re-use capabilities.
 * 
 * ✅ FIX: Uses axiosInstance (with auth interceptors) instead of raw fetch.
 * ✅ FIX: Uses correct query params matching ConversationsController (take/skip).
 */
const QueryHistory = ({ connectionId, onReuse }) => {
  const [history, setHistory] = useState([]);
  const [favorites, setFavorites] = useState(() => {
    try { return JSON.parse(localStorage.getItem('tts_favorites') || '[]'); }
    catch { return []; }
  });
  const [filter, setFilter] = useState('all');
  const [searchTerm, setSearchTerm] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  const fetchHistory = useCallback(async () => {
    setIsLoading(true);
    try {
      // ✅ FIX: Use axiosInstance (handles auth, base URL, token refresh)
      // ConversationsController.GetConversations uses skip/take, not limit
      const response = await axiosInstance.get(API_ENDPOINTS.CONVERSATIONS, {
        params: { take: 50, skip: 0 },
      });

      // API returns ConversationSummary[] with: id, connectionId, title, messageCount, lastQuery, createdAt
      let conversations = response.data;
      if (Array.isArray(conversations)) {
        // Filter by connectionId if provided
        if (connectionId) {
          conversations = conversations.filter(c => c.connectionId === connectionId);
        }
        setHistory(conversations);
      }
    } catch (err) {
      console.warn('[QueryHistory] Failed to fetch:', err.message);
    } finally {
      setIsLoading(false);
    }
  }, [connectionId]);

  useEffect(() => { fetchHistory(); }, [fetchHistory]);

  const toggleFavorite = (item) => {
    const id = item.id;
    const newFavs = favorites.includes(id)
      ? favorites.filter(f => f !== id)
      : [...favorites, id];
    setFavorites(newFavs);
    localStorage.setItem('tts_favorites', JSON.stringify(newFavs));
  };

  const filteredHistory = history.filter(item => {
    if (filter === 'favorites' && !favorites.includes(item.id)) return false;
    if (searchTerm) {
      const q = (item.lastQuery || item.title || '').toLowerCase();
      if (!q.includes(searchTerm.toLowerCase())) return false;
    }
    return true;
  });

  return (
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column', padding: '8px' }}>
      {/* Search */}
      <Input
        placeholder="Search queries..."
        prefix={<SearchOutlined />}
        value={searchTerm}
        onChange={e => setSearchTerm(e.target.value)}
        style={{ marginBottom: 12 }}
      />

      {/* Filter Buttons */}
      <div style={{ display: 'flex', gap: 8, marginBottom: 12, flexWrap: 'wrap' }}>
        <Button size="small" type={filter === 'all' ? 'primary' : 'default'} onClick={() => setFilter('all')}>
          All ({history.length})
        </Button>
        <Button
          size="small"
          type={filter === 'favorites' ? 'primary' : 'default'}
          icon={<StarFilled />}
          onClick={() => setFilter('favorites')}
        >
          Favorites
        </Button>
      </div>

      {/* List */}
      <div style={{ flex: 1, overflowY: 'auto' }}>
        {isLoading ? (
          <div style={{ textAlign: 'center', padding: 24 }}>
            <Spin />
          </div>
        ) : filteredHistory.length === 0 ? (
          <Empty description={searchTerm ? 'No matches' : 'No query history'} />
        ) : (
          filteredHistory.map((item, idx) => {
            const isFav = favorites.includes(item.id);
            return (
              <div
                key={item.id || idx}
                style={{
                  padding: 12, marginBottom: 8,
                  border: '1px solid #f0f0f0', borderRadius: 8,
                  backgroundColor: '#fafafa', cursor: 'pointer',
                  transition: 'all 0.2s',
                }}
                onMouseEnter={e => e.currentTarget.style.backgroundColor = '#f5f5f5'}
                onMouseLeave={e => e.currentTarget.style.backgroundColor = '#fafafa'}
                onClick={() => onReuse?.(item.lastQuery || item.title)}
              >
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
                  <div style={{ flex: 1, overflow: 'hidden' }}>
                    <div style={{ fontWeight: 500, marginBottom: 4, color: '#262626' }}>
                      {item.title || 'Untitled conversation'}
                    </div>
                    {/* Show last query from ConversationSummary.LastQuery */}
                    {item.lastQuery && (
                      <div style={{
                        fontSize: 12, color: '#8c8c8c', margin: '4px 0',
                        whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
                      }}>
                        💬 {item.lastQuery}
                      </div>
                    )}
                    <div style={{ fontSize: 11, color: '#8c8c8c', marginTop: 4 }}>
                      {item.createdAt ? new Date(item.createdAt).toLocaleDateString() : ''}
                      {' • '}
                      {item.messageCount ?? 0} messages
                    </div>
                  </div>
                  <div style={{ display: 'flex', gap: 4, marginLeft: 8 }}>
                    <Button
                      type="text" size="small"
                      icon={isFav ? <StarFilled style={{ color: '#faad14' }} /> : <StarOutlined />}
                      onClick={(e) => { e.stopPropagation(); toggleFavorite(item); }}
                    />
                    <Button
                      type="text" size="small"
                      icon={<ReloadOutlined />}
                      title="Re-use this query"
                      onClick={(e) => { e.stopPropagation(); onReuse?.(item.lastQuery || item.title); }}
                    />
                  </div>
                </div>
              </div>
            );
          })
        )}
      </div>
    </div>
  );
};

export default QueryHistory;
