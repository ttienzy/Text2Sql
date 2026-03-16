/**
 * SkeletonErrorBoundary - Error boundary for skeleton components
 * Provides fallback UI if skeleton components fail to render
 */
import React from 'react';
import { Skeleton } from 'antd';

class SkeletonErrorBoundary extends React.Component {
    constructor(props) {
        super(props);
        this.state = { hasError: false };
    }

    static getDerivedStateFromError(error) {
        return { hasError: true };
    }

    componentDidCatch(error, errorInfo) {
        console.warn('Skeleton component error:', error, errorInfo);
    }

    render() {
        if (this.state.hasError) {
            // Fallback to simple skeleton
            return (
                <div style={{ padding: 16 }}>
                    <Skeleton active paragraph={{ rows: 3 }} />
                </div>
            );
        }

        return this.props.children;
    }
}

export default SkeletonErrorBoundary;