import { useState, useCallback, useRef } from 'react';

const API_BASE = import.meta.env.VITE_API_BASE_URL || 'https://localhost:7189';

/**
 * LARGE-1c: Real SSE streaming hook for agent query processing.
 * Connects to POST /api/v2/agent/process/stream and emits real-time stage updates.
 * 
 * ✅ FIX: Reads token from authStore (memory-only) instead of localStorage.
 * ✅ FIX: Supports connectionId parameter.
 */
export const useStreamingQuery = () => {
  const [stages, setStages] = useState([]);
  const [currentStage, setCurrentStage] = useState(null);
  const [progress, setProgress] = useState(0);
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);
  const [isStreaming, setIsStreaming] = useState(false);
  const [isComplete, setIsComplete] = useState(false);
  const [sqlTokens, setSqlTokens] = useState([]);
  const [generatedSql, setGeneratedSql] = useState('');
  const abortRef = useRef(null);

  const reset = useCallback(() => {
    setStages([]);
    setCurrentStage(null);
    setProgress(0);
    setResult(null);
    setError(null);
    setIsStreaming(false);
    setIsComplete(false);
    setSqlTokens([]);
    setGeneratedSql('');
  }, []);

  /**
   * Start a streaming query via SSE (Server-Sent Events over fetch).
   * Uses fetch + ReadableStream because EventSource does not support POST.
   * ✅ FIX: Added token refresh on 401
   */
  const startStream = useCallback(async (question, conversationId, connectionId) => {
    reset();
    setIsStreaming(true);

    const controller = new AbortController();
    abortRef.current = controller;

    try {
      // ✅ FIX: Read token from authStore (memory-only) instead of localStorage
      const { default: useAuthStore } = await import('../store/authStore');
      let token = useAuthStore.getState().accessToken;

      // ✅ FIX: If no token, try to initialize auth first
      if (!token) {
        console.log('[useStreamingQuery] No token found, attempting to initialize auth...');
        await useAuthStore.getState().initializeAuth();
        token = useAuthStore.getState().accessToken;

        if (!token) {
          throw new Error('Authentication required. Please log in again.');
        }
      }

      const makeRequest = async (authToken) => {
        const response = await fetch(`${API_BASE}/api/v2/agent/process/stream`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${authToken}`,
          },
          body: JSON.stringify({ question, conversationId, connectionId }),
          signal: controller.signal,
        });

        // ✅ FIX: Handle 401 by refreshing token and retrying
        if (response.status === 401) {
          console.log('[useStreamingQuery] 401 Unauthorized, attempting token refresh...');

          // Try to refresh token
          const refreshToken = localStorage.getItem('tts_refresh_token');
          if (!refreshToken) {
            throw new Error('Session expired. Please log in again.');
          }

          try {
            const refreshResponse = await fetch(`${API_BASE}/api/auth/refresh`, {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ refreshToken }), // camelCase - backend auto-converts
            });

            if (!refreshResponse.ok) {
              throw new Error('Token refresh failed');
            }

            const { accessToken, refreshToken: newRefreshToken } = await refreshResponse.json();

            // Update authStore
            useAuthStore.getState().setToken(accessToken, newRefreshToken || refreshToken);

            console.log('[useStreamingQuery] Token refreshed, retrying request...');

            // Retry with new token
            return makeRequest(accessToken);
          } catch (refreshError) {
            console.error('[useStreamingQuery] Token refresh failed:', refreshError);
            // Force logout
            useAuthStore.getState().forceLogout();
            throw new Error('Session expired. Please log in again.');
          }
        }

        if (!response.ok) {
          throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        }

        return response;
      };

      const response = await makeRequest(token);

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });

        // Parse SSE events from the buffer
        const events = buffer.split('\n\n');
        buffer = events.pop() || ''; // Keep incomplete event in buffer

        for (const eventBlock of events) {
          if (!eventBlock.trim()) continue;

          let eventType = 'message';
          let eventData = '';

          for (const line of eventBlock.split('\n')) {
            if (line.startsWith('event: ')) {
              eventType = line.slice(7).trim();
            } else if (line.startsWith('data: ')) {
              eventData = line.slice(6);
            }
          }

          if (!eventData) continue;

          try {
            const data = JSON.parse(eventData);

            switch (eventType) {
              case 'sql_token':
                // ✅ NEW: Handle SQL token streaming
                setSqlTokens(prev => [...prev, data.token]);
                setGeneratedSql(prev => prev + data.token);
                break;

              case 'stage_update':
                setCurrentStage(data);
                setProgress(Math.round((data.progress || 0) * 100));
                setStages(prev => {
                  const idx = prev.findIndex(s => s.stage === data.stage);
                  if (idx >= 0) {
                    const updated = [...prev];
                    updated[idx] = data;
                    return updated;
                  }
                  return [...prev, data];
                });
                break;

              case 'result':
                setResult(data);
                setIsComplete(true);
                setIsStreaming(false);
                setProgress(100);
                break;

              case 'error':
                setError(data);
                setIsStreaming(false);
                break;

              default:
                break;
            }
          } catch {
            // Skip malformed JSON
          }
        }
      }

      // If stream ended without a result event, mark as complete
      setIsStreaming(false);
    } catch (err) {
      if (err.name === 'AbortError') return;
      console.error('[useStreamingQuery] Error:', err);
      setError({ code: 'STREAM_ERROR', message: err.message });
      setIsStreaming(false);
    }
  }, [reset]);

  const cancel = useCallback(() => {
    if (abortRef.current) {
      abortRef.current.abort();
      abortRef.current = null;
    }
    setIsStreaming(false);
  }, []);

  return {
    stages,
    currentStage,
    progress,
    result,
    error,
    isStreaming,
    isComplete,
    sqlTokens,
    generatedSql,
    startStream,
    cancel,
    reset,
  };
};

export default useStreamingQuery;
