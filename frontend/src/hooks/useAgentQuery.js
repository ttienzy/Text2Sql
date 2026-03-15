import { useState, useCallback, useRef, useEffect } from 'react';
import { API_BASE_URL, API_ENDPOINTS } from '../constants';

/**
 * Connection states for the API request
 */
export const ConnectionState = {
  IDLE: 'idle',
  CONNECTING: 'connecting',
  CONNECTED: 'connected',
  ERROR: 'error',
  CLOSED: 'closed',
  STREAMING: 'streaming',
};

/**
 * Agent step event types from the backend
 */
export const AgentStepEventType = {
  AGENT_STARTED: 'agent_started',
  TOOL_SELECTED: 'tool_selected',
  TOOL_EXECUTING: 'tool_executing',
  TOOL_RESULT: 'tool_result',
  REFLECTING: 'reflecting',
  SQL_GENERATED: 'sql_generated',
  SQL_EXECUTING: 'sql_executing',
  COMPLETED: 'completed',
  ERROR: 'error',
  NEEDS_CLARIFICATION: 'needs_clarification',
  DML_CONFIRMED: 'dml_confirmed',
  DML_REJECTED: 'dml_rejected',
};

/**
 * Maps event types to human-readable step names
 */
export const StepNameMap = {
  [AgentStepEventType.AGENT_STARTED]: 'Starting agent...',
  [AgentStepEventType.TOOL_SELECTED]: 'Selecting tool...',
  [AgentStepEventType.TOOL_EXECUTING]: 'Executing tool...',
  [AgentStepEventType.TOOL_RESULT]: 'Processing result...',
  [AgentStepEventType.REFLECTING]: 'Analyzing...',
  [AgentStepEventType.SQL_GENERATED]: 'Generating SQL...',
  [AgentStepEventType.SQL_EXECUTING]: 'Executing SQL...',
  [AgentStepEventType.COMPLETED]: 'Completed',
  [AgentStepEventType.ERROR]: 'Error',
  [AgentStepEventType.NEEDS_CLARIFICATION]: 'Needs clarification',
};

/**
 * Maps event types to step categories for progress display
 */
export const StepCategoryMap = {
  [AgentStepEventType.AGENT_STARTED]: 'idle',
  [AgentStepEventType.TOOL_SELECTED]: 'tool_selection',
  [AgentStepEventType.TOOL_EXECUTING]: 'tool_execution',
  [AgentStepEventType.TOOL_RESULT]: 'tool_execution',
  [AgentStepEventType.REFLECTING]: 'analysis',
  [AgentStepEventType.SQL_GENERATED]: 'sql_generation',
  [AgentStepEventType.SQL_EXECUTING]: 'sql_execution',
  [AgentStepEventType.COMPLETED]: 'completed',
  [AgentStepEventType.ERROR]: 'error',
  [AgentStepEventType.NEEDS_CLARIFICATION]: 'clarification',
};

/**
 * Custom hook for agent query responses via REST API
 * 
 * @returns {Object} - { events, connectionState, error, start, stop, clear, latestEvent, isStreaming, currentStep }
 */
export const useAgentQuery = () => {
  const [events, setEvents] = useState([]);
  const [connectionState, setConnectionState] = useState(ConnectionState.IDLE);
  const [error, setError] = useState(null);
  
  // Use refs to store dynamic parameters
  const questionRef = useRef('');
  const conversationIdRef = useRef(null);
  const abortControllerRef = useRef(null);

  // Clean up on unmount
  useEffect(() => {
    return () => {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, []);

  /**
   * Execute agent query via REST API
   */
  const start = useCallback(async (options = {}) => {
    // Update refs with provided options
    const { question: q, conversationId: cid } = options;
    questionRef.current = q;
    conversationIdRef.current = cid;
    
    // Reset state
    setEvents([]);
    setError(null);
    setConnectionState(ConnectionState.CONNECTING);

    try {
      // Get auth token from localStorage
      const token = localStorage.getItem('tts_access_token');
      if (!token) {
        throw new Error('Authentication required. Please log in.');
      }

      // Use REST API endpoint with explicit timeout
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 300000); // 5 minute timeout
      
      try {
        const response = await fetch(
          `${API_BASE_URL}${API_ENDPOINTS.AGENT}/query`,
          {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              'Authorization': `Bearer ${token}`,
            },
            body: JSON.stringify({
              Question: questionRef.current,
              ConversationId: conversationIdRef.current,
            }),
            signal: controller.signal,
          }
        );
        
        clearTimeout(timeoutId);

        if (!response.ok) {
          const errorData = await response.json().catch(() => ({}));
          throw new Error(errorData.message || errorData.errorMessage || `Server error: ${response.status}`);
        }

        const result = await response.json();
        setConnectionState(ConnectionState.CONNECTED);

        // Convert REST response to events format
        const newEvents = [];

        // Add agent_started event
        newEvents.push({
          type: 'agent_started',
          thought: 'Processing your query...',
        });

        // Add sql_generated event if SQL was generated
        if (result.sqlGenerated) {
          newEvents.push({
            type: 'sql_generated',
            sqlGenerated: result.sqlGenerated,
            thought: 'SQL query generated',
          });
        }

        // Add completion event
        newEvents.push({
          type: 'completed',
          answer: result.answer || result.ErrorMessage || 'Query completed',
          sqlGenerated: result.sqlGenerated,
          result: result.result,
          rowCount: result.rowCount,
          success: result.success,
        });

        setEvents(newEvents);
        setConnectionState(ConnectionState.CLOSED);
      } catch (fetchError) {
        clearTimeout(timeoutId);
        throw fetchError;
      }
    } catch (err) {
      // Handle abort
      if (err.name === 'AbortError') {
        setConnectionState(ConnectionState.CLOSED);
        return;
      }
      
      console.error('Agent query error:', err);
      setConnectionState(ConnectionState.ERROR);
      setError(err.message || 'Failed to execute query');
    }
  }, []);

  /**
   * Execute agent query via Streaming API (SSE)
   * Uses /api/agent/stream-query endpoint for real-time progress updates
   * 
   * @param {Object} options - Query options
   * @param {string} options.question - The natural language question
   * @param {string} options.conversationId - Optional conversation ID
   * @param {Function} options.onProgress - Optional callback for each progress update
   * @returns {Promise<void>}
   */
  const startStreaming = useCallback(async (options = {}) => {
    const { question: q, conversationId: cid, onProgress } = options;
    questionRef.current = q;
    conversationIdRef.current = cid;
    
    // Reset state
    setEvents([]);
    setError(null);
    setConnectionState(ConnectionState.STREAMING);

    try {
      // Get auth token from localStorage
      const token = localStorage.getItem('tts_access_token');
      if (!token) {
        throw new Error('Authentication required. Please log in.');
      }

      // Create abort controller for cancellation
      abortControllerRef.current = new AbortController();

      // Use streaming API endpoint
      const response = await fetch(
        `${API_BASE_URL}${API_ENDPOINTS.AGENT}/stream-query`,
        {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Accept': 'text/event-stream',
            'Cache-Control': 'no-cache',
            'Authorization': `Bearer ${token}`,
          },
          body: JSON.stringify({
            Question: questionRef.current,
            ConversationId: conversationIdRef.current,
          }),
          signal: abortControllerRef.current.signal,
        }
      );

      if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.message || errorData.errorMessage || `Server error: ${response.status}`);
      }

      if (!response.body) {
        throw new Error('Response body is not available');
      }

      // Set up reader for streaming response
      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      setConnectionState(ConnectionState.STREAMING);

      while (true) {
        const { done, value } = await reader.read();
        
        if (done) {
          // Process any remaining data in buffer
          if (buffer.trim()) {
            const event = parseSSEEvent(buffer);
            if (event) {
              handleEvent(event, onProgress);
            }
          }
          break;
        }

        // Decode chunk and append to buffer
        buffer += decoder.decode(value, { stream: true });

        // Process complete SSE events (ending with \n\n)
        const events = buffer.split('\n\n');
        
        // Keep the last incomplete event in buffer
        buffer = events.pop() || '';

        // Process each complete event
        for (const eventData of events) {
          const event = parseSSEEvent(eventData);
          if (event) {
            handleEvent(event, onProgress);
          }
        }
      }

      setConnectionState(ConnectionState.CLOSED);
    } catch (err) {
      // Handle abort
      if (err.name === 'AbortError') {
        setConnectionState(ConnectionState.CLOSED);
        return;
      }
      
      console.error('Agent streaming error:', err);
      setConnectionState(ConnectionState.ERROR);
      setError(err.message || 'Failed to execute streaming query');
    }
  }, []);

  /**
   * Parse a single SSE event from raw string
   * SSE format: "data: {json}\n\n"
   * 
   * @param {string} eventData - Raw SSE event string
   * @returns {Object|null} - Parsed event object or null if invalid
   */
  const parseSSEEvent = (eventData) => {
    if (!eventData || typeof eventData !== 'string') {
      return null;
    }

    const lines = eventData.split('\n');
    let data = null;

    for (const line of lines) {
      // Look for "data:" prefix
      if (line.startsWith('data:')) {
        const dataStr = line.substring(5).trim();
        if (dataStr) {
          try {
            data = JSON.parse(dataStr);
          } catch {
            console.warn('Failed to parse SSE data:', dataStr);
            // Return raw string if not valid JSON
            data = { raw: dataStr };
          }
        }
        break;
      }
    }

    return data;
  };

  /**
   * Handle a parsed event - add to events array and call progress callback
   * 
   * @param {Object} event - Parsed event object
   * @param {Function} onProgress - Optional progress callback
   */
  const handleEvent = (event, onProgress) => {
    if (!event) return;

    // Normalize event format
    const normalizedEvent = normalizeEvent(event);
    
    // Add to events array
    setEvents(prev => {
      const newEvents = [...prev, normalizedEvent];
      return newEvents;
    });

    // Call progress callback if provided
    if (onProgress && typeof onProgress === 'function') {
      onProgress(normalizedEvent);
    }
  };

  /**
   * Normalize event format from various sources
   * 
   * @param {Object} event - Raw event from API
   * @returns {Object} - Normalized event object
   */
  const normalizeEvent = (event) => {
    // Handle different event formats
    if (event.type) {
      // Already in correct format
      return event;
    }

    if (event.event) {
      // Backend uses 'event' field
      return {
        type: event.event,
        thought: event.thought || event.message,
        step: event.step,
        totalSteps: event.totalSteps,
        maxSteps: event.maxSteps,
        toolName: event.toolName,
        toolResult: event.toolResult,
        sqlGenerated: event.sqlGenerated || event.sql,
        sqlResult: event.sqlResult,
        result: event.result,
        rowCount: event.rowCount,
        answer: event.answer,
        errorMessage: event.error || event.errorMessage,
        data: event.data,
      };
    }

    // Unknown format, return as-is
    return event;
  };

  /**
   * Stop/Cancel the current query
   */
  const stop = useCallback(() => {
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
      abortControllerRef.current = null;
    }
    setConnectionState(ConnectionState.CLOSED);
  }, []);

  /**
   * Clear all events
   */
  const clear = useCallback(() => {
    setEvents([]);
    setError(null);
    setConnectionState(ConnectionState.IDLE);
  }, []);

  /**
   * Get the latest event
   */
  const latestEvent = events.length > 0 ? events[events.length - 1] : null;

  /**
   * Check if currently processing (including streaming)
   */
  const isProcessing = connectionState === ConnectionState.CONNECTING || 
                       connectionState === ConnectionState.CONNECTED ||
                       connectionState === ConnectionState.STREAMING;

  /**
   * Check if currently streaming
   */
  const isStreaming = connectionState === ConnectionState.STREAMING;

  /**
   * Get current step info for progress display
   */
  const currentStep = latestEvent ? {
    type: latestEvent.type,
    stepNumber: latestEvent.step,
    totalSteps: latestEvent.totalSteps,
    maxSteps: latestEvent.maxSteps,
    thought: latestEvent.thought,
    toolName: latestEvent.toolName,
    toolResult: latestEvent.toolResult,
    sqlGenerated: latestEvent.sqlGenerated,
    sqlResult: latestEvent.sqlResult,
    rowCount: latestEvent.rowCount,
    answer: latestEvent.answer,
    errorMessage: latestEvent.errorMessage,
    category: StepCategoryMap[latestEvent.type] || 'unknown',
    stepName: StepNameMap[latestEvent.type] || 'Processing...',
  } : null;

  return {
    events,
    connectionState,
    error,
    start,
    startStreaming,
    stop,
    clear,
    latestEvent,
    isProcessing,
    isStreaming,
    currentStep,
  };
};

export default useAgentQuery;
