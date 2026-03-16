/**
 * useDelayedLoading Hook
 * Prevents skeleton flashing for very fast loading operations
 * Only shows skeleton if loading takes longer than the specified delay
 */
import { useState, useEffect } from 'react';

export const useDelayedLoading = (isLoading, delay = 200) => {
    const [showSkeleton, setShowSkeleton] = useState(false);

    useEffect(() => {
        let timeoutId;

        if (isLoading) {
            // Start timer to show skeleton after delay
            timeoutId = setTimeout(() => {
                setShowSkeleton(true);
            }, delay);
        } else {
            // Hide skeleton immediately when loading stops
            setShowSkeleton(false);
        }

        return () => {
            if (timeoutId) {
                clearTimeout(timeoutId);
            }
        };
    }, [isLoading, delay]);

    return showSkeleton;
};