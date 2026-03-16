/**
 * Responsive Skeleton Components
 * Adapts skeleton count based on screen size for better performance
 */
import { useState, useEffect } from 'react';
import { ChatMessageSkeleton, ConnectionCardSkeleton, ConversationListSkeleton } from './SkeletonLoaders';
import SkeletonErrorBoundary from './SkeletonErrorBoundary';

/**
 * Hook to get responsive skeleton counts based on screen size
 */
const useResponsiveSkeletonCount = () => {
    const [counts, setCounts] = useState({
        messages: 3,
        connections: 6,
        conversations: 5,
    });

    useEffect(() => {
        const updateCounts = () => {
            const width = window.innerWidth;

            if (width < 768) {
                // Mobile
                setCounts({
                    messages: 2,
                    connections: 2,
                    conversations: 3,
                });
            } else if (width < 1200) {
                // Tablet
                setCounts({
                    messages: 3,
                    connections: 4,
                    conversations: 4,
                });
            } else {
                // Desktop
                setCounts({
                    messages: 3,
                    connections: 6,
                    conversations: 5,
                });
            }
        };

        updateCounts();
        window.addEventListener('resize', updateCounts);
        return () => window.removeEventListener('resize', updateCounts);
    }, []);

    return counts;
};

/**
 * Responsive Chat Message Skeleton
 */
export const ResponsiveChatMessageSkeleton = () => {
    const { messages } = useResponsiveSkeletonCount();
    return (
        <SkeletonErrorBoundary>
            <ChatMessageSkeleton count={messages} />
        </SkeletonErrorBoundary>
    );
};

/**
 * Responsive Connection Card Skeleton
 */
export const ResponsiveConnectionCardSkeleton = () => {
    const { connections } = useResponsiveSkeletonCount();
    return (
        <SkeletonErrorBoundary>
            <ConnectionCardSkeleton count={connections} />
        </SkeletonErrorBoundary>
    );
};

/**
 * Responsive Conversation List Skeleton
 */
export const ResponsiveConversationListSkeleton = () => {
    const { conversations } = useResponsiveSkeletonCount();
    return (
        <SkeletonErrorBoundary>
            <ConversationListSkeleton count={conversations} />
        </SkeletonErrorBoundary>
    );
};