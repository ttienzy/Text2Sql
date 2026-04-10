import { useCallback, useReducer, useRef } from 'react';

const API_BASE = import.meta.env.VITE_API_BASE_URL || 'https://localhost:7189';

const initialState = {
  stages: [],
  currentStage: null,
  progress: 0,
  result: null,
  error: null,
  isStreaming: false,
  isComplete: false,
  sqlTokens: [],
  generatedSql: '',
};

function upsertStage(stages, stage) {
  const index = stages.findIndex((item) => item.stage === stage.stage);
  if (index === -1) {
    return [...stages, stage];
  }

  const nextStages = [...stages];
  nextStages[index] = stage;
  return nextStages;
}

function streamReducer(state, action) {
  switch (action.type) {
    case 'RESET':
      return initialState;

    case 'START':
      return {
        ...initialState,
        isStreaming: true,
      };

    case 'SQL_TOKEN':
      return {
        ...state,
        sqlTokens: [...state.sqlTokens, action.token],
        generatedSql: state.generatedSql + action.token,
      };

    case 'STAGE_UPDATE':
      return {
        ...state,
        currentStage: action.stage,
        progress: Math.round((action.stage.progress || 0) * 100),
        stages: upsertStage(state.stages, action.stage),
      };

    case 'RESULT':
      return {
        ...state,
        result: action.result,
        error: null,
        isStreaming: false,
        isComplete: true,
        progress: 100,
      };

    case 'ERROR':
      return {
        ...state,
        error: action.error,
        isStreaming: false,
      };

    case 'COMPLETE':
      return {
        ...state,
        isStreaming: false,
        isComplete: true,
      };

    default:
      return state;
  }
}

export const useStreamingQuery = () => {
  const [state, dispatch] = useReducer(streamReducer, initialState);
  const abortRef = useRef(null);

  const reset = useCallback(() => {
    dispatch({ type: 'RESET' });
  }, []);

  const startStream = useCallback(async (question, conversationId, connectionId) => {
    dispatch({ type: 'START' });

    const controller = new AbortController();
    abortRef.current = controller;

    try {
      const { default: useAuthStore } = await import('../store/authStore');
      let token = useAuthStore.getState().accessToken;

      if (!token) {
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

        if (response.status === 401) {
          const refreshToken = localStorage.getItem('tts_refresh_token');
          if (!refreshToken) {
            throw new Error('Session expired. Please log in again.');
          }

          try {
            const refreshResponse = await fetch(`${API_BASE}/api/auth/refresh`, {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ refreshToken }),
            });

            if (!refreshResponse.ok) {
              throw new Error('Token refresh failed');
            }

            const { accessToken, refreshToken: newRefreshToken } = await refreshResponse.json();
            useAuthStore.getState().setToken(accessToken, newRefreshToken || refreshToken);
            return makeRequest(accessToken);
          } catch (refreshError) {
            console.error('[useStreamingQuery] Token refresh failed:', refreshError);
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
        if (done) {
          break;
        }

        buffer += decoder.decode(value, { stream: true });
        const events = buffer.split('\n\n');
        buffer = events.pop() || '';

        for (const eventBlock of events) {
          if (!eventBlock.trim()) {
            continue;
          }

          let eventType = 'message';
          let eventData = '';

          for (const line of eventBlock.split('\n')) {
            if (line.startsWith('event: ')) {
              eventType = line.slice(7).trim();
            } else if (line.startsWith('data: ')) {
              eventData = line.slice(6);
            }
          }

          if (!eventData) {
            continue;
          }

          try {
            const data = JSON.parse(eventData);

            switch (eventType) {
              case 'sql_token':
                dispatch({ type: 'SQL_TOKEN', token: data.token || '' });
                break;

              case 'stage_update':
                dispatch({ type: 'STAGE_UPDATE', stage: data });
                break;

              case 'result':
                dispatch({ type: 'RESULT', result: data });
                break;

              case 'error':
                dispatch({ type: 'ERROR', error: data });
                break;

              case 'turn_failed':
                break;

              case 'turn_completed':
                break;

              case 'turn_started':
              default:
                break;
            }
          } catch (parseError) {
            console.error('[useStreamingQuery] Failed to parse SSE event:', parseError);
          }
        }
      }

      dispatch({ type: 'COMPLETE' });
    } catch (err) {
      if (err.name === 'AbortError') {
        return;
      }

      console.error('[useStreamingQuery] Error:', err);
      dispatch({
        type: 'ERROR',
        error: { code: 'STREAM_ERROR', message: err.message },
      });
    }
  }, []);

  const cancel = useCallback(() => {
    if (abortRef.current) {
      abortRef.current.abort();
      abortRef.current = null;
    }

    dispatch({ type: 'COMPLETE' });
  }, []);

  return {
    ...state,
    startStream,
    cancel,
    reset,
  };
};

export default useStreamingQuery;
