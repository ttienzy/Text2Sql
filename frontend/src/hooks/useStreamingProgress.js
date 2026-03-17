import { useState, useEffect, useRef } from 'react';

/**
 * Custom hook for streaming progress updates
 * Can be extended to work with Server-Sent Events (SSE) or WebSocket
 * Currently simulates streaming for better UX
 */
export const useStreamingProgress = (isActive = false, question = '') => {
    const [currentStep, setCurrentStep] = useState(null);
    const [progress, setProgress] = useState(0);
    const [elapsedTime, setElapsedTime] = useState(0);
    const [isComplete, setIsComplete] = useState(false);
    const intervalRef = useRef(null);
    const startTimeRef = useRef(null);

    // Simulated streaming steps - can be replaced with real API streaming
    const streamingSteps = [
        {
            id: 'understanding',
            title: 'Understanding Question',
            description: 'Analyzing natural language query...',
            duration: 1000,
            progress: 15,
        },
        {
            id: 'schema_retrieval',
            title: 'Schema Retrieval',
            description: 'Fetching relevant database schema...',
            duration: 800,
            progress: 30,
        },
        {
            id: 'planning',
            title: 'Query Planning',
            description: 'Planning SQL query structure...',
            duration: 1200,
            progress: 50,
        },
        {
            id: 'generation',
            title: 'SQL Generation',
            description: 'Generating optimized SQL query...',
            duration: 900,
            progress: 75,
        },
        {
            id: 'execution',
            title: 'Query Execution',
            description: 'Executing query against database...',
            duration: 600,
            progress: 90,
        },
        {
            id: 'formatting',
            title: 'Result Formatting',
            description: 'Formatting results and response...',
            duration: 400,
            progress: 100,
        },
    ];

    // Reset when starting new query
    useEffect(() => {
        if (isActive && question) {
            setCurrentStep(null);
            setProgress(0);
            setElapsedTime(0);
            setIsComplete(false);
            startTimeRef.current = Date.now();

            // Start simulation
            simulateStreaming();
        } else {
            // Clean up when not active
            if (intervalRef.current) {
                clearInterval(intervalRef.current);
                intervalRef.current = null;
            }
        }

        return () => {
            if (intervalRef.current) {
                clearInterval(intervalRef.current);
            }
        };
    }, [isActive, question]);

    // Simulate streaming progress
    const simulateStreaming = () => {
        let stepIndex = 0;
        let stepStartTime = Date.now();

        const updateProgress = () => {
            const now = Date.now();
            const totalElapsed = now - startTimeRef.current;
            setElapsedTime(totalElapsed);

            if (stepIndex >= streamingSteps.length) {
                setIsComplete(true);
                setProgress(100);
                if (intervalRef.current) {
                    clearInterval(intervalRef.current);
                    intervalRef.current = null;
                }
                return;
            }

            const currentStepConfig = streamingSteps[stepIndex];
            const stepElapsed = now - stepStartTime;
            const stepProgress = Math.min(stepElapsed / currentStepConfig.duration, 1);

            // Update current step
            setCurrentStep({
                ...currentStepConfig,
                stepProgress,
                isActive: stepProgress < 1,
            });

            // Update overall progress
            const baseProgress = stepIndex > 0 ? streamingSteps[stepIndex - 1].progress : 0;
            const nextProgress = currentStepConfig.progress;
            const currentProgress = baseProgress + (nextProgress - baseProgress) * stepProgress;
            setProgress(currentProgress);

            // Move to next step
            if (stepProgress >= 1) {
                stepIndex++;
                stepStartTime = now;
            }
        };

        intervalRef.current = setInterval(updateProgress, 100);
    };

    // Future: Connect to real streaming API
    const connectToStream = (apiEndpoint, requestData) => {
        // Example implementation for Server-Sent Events
        /*
        const eventSource = new EventSource(`${apiEndpoint}?${new URLSearchParams(requestData)}`);
        
        eventSource.onmessage = (event) => {
          const data = JSON.parse(event.data);
          
          if (data.type === 'step_update') {
            setCurrentStep({
              id: data.stepId,
              title: data.stepTitle,
              description: data.description,
              progress: data.progress,
              isActive: true,
            });
            setProgress(data.overallProgress);
          }
          
          if (data.type === 'complete') {
            setIsComplete(true);
            setProgress(100);
            eventSource.close();
          }
        };
        
        eventSource.onerror = () => {
          eventSource.close();
          // Fallback to simulation
          simulateStreaming();
        };
        
        return () => eventSource.close();
        */
    };

    // Future: Connect to WebSocket
    const connectToWebSocket = (wsEndpoint, requestData) => {
        // Example implementation for WebSocket
        /*
        const ws = new WebSocket(wsEndpoint);
        
        ws.onopen = () => {
          ws.send(JSON.stringify(requestData));
        };
        
        ws.onmessage = (event) => {
          const data = JSON.parse(event.data);
          // Handle progress updates similar to SSE
        };
        
        ws.onerror = () => {
          ws.close();
          // Fallback to simulation
          simulateStreaming();
        };
        
        return () => ws.close();
        */
    };

    return {
        currentStep,
        progress: Math.round(progress),
        elapsedTime,
        isComplete,
        // Future methods for real streaming
        connectToStream,
        connectToWebSocket,
    };
};

export default useStreamingProgress;