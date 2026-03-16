import React from 'react';
import { Result, Button, Typography } from 'antd';
import { ReloadOutlined, HomeOutlined } from '@ant-design/icons';

const { Paragraph, Text } = Typography;

/**
 * Error Boundary Component
 * Catches JavaScript errors anywhere in the child component tree
 */
class ErrorBoundary extends React.Component {
    constructor(props) {
        super(props);
        this.state = {
            hasError: false,
            error: null,
            errorInfo: null
        };
    }

    static getDerivedStateFromError(error) {
        // Update state so the next render will show the fallback UI
        return { hasError: true };
    }

    componentDidCatch(error, errorInfo) {
        // Log error details
        console.error('ErrorBoundary caught an error:', error, errorInfo);

        this.setState({
            error,
            errorInfo
        });

        // You can also log the error to an error reporting service here
        // Example: logErrorToService(error, errorInfo);
    }

    handleReload = () => {
        window.location.reload();
    };

    handleGoHome = () => {
        window.location.href = '/';
    };

    render() {
        if (this.state.hasError) {
            // Custom fallback UI
            return (
                <div style={{
                    minHeight: '100vh',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    padding: '20px'
                }}>
                    <Result
                        status="error"
                        title="Something went wrong"
                        subTitle="An unexpected error occurred. Please try reloading the page or contact support if the problem persists."
                        extra={[
                            <Button
                                type="primary"
                                icon={<ReloadOutlined />}
                                onClick={this.handleReload}
                                key="reload"
                            >
                                Reload Page
                            </Button>,
                            <Button
                                icon={<HomeOutlined />}
                                onClick={this.handleGoHome}
                                key="home"
                            >
                                Go Home
                            </Button>,
                        ]}
                    >
                        {process.env.NODE_ENV === 'development' && (
                            <div style={{ textAlign: 'left', marginTop: 16 }}>
                                <Typography.Title level={5}>Error Details (Development Only):</Typography.Title>
                                <Paragraph>
                                    <Text code>{this.state.error && this.state.error.toString()}</Text>
                                </Paragraph>
                                {this.state.errorInfo && (
                                    <Paragraph>
                                        <Text code style={{ whiteSpace: 'pre-wrap' }}>
                                            {this.state.errorInfo.componentStack}
                                        </Text>
                                    </Paragraph>
                                )}
                            </div>
                        )}
                    </Result>
                </div>
            );
        }

        // If no error, render children normally
        return this.props.children;
    }
}

/**
 * Hook-based Error Boundary for functional components
 * Note: This is a wrapper around the class-based ErrorBoundary
 */
export const withErrorBoundary = (Component, fallback) => {
    return function WrappedComponent(props) {
        return (
            <ErrorBoundary fallback={fallback}>
                <Component {...props} />
            </ErrorBoundary>
        );
    };
};

/**
 * Simple Error Fallback Component
 */
export const ErrorFallback = ({ error, resetError }) => (
    <Result
        status="error"
        title="Component Error"
        subTitle="This component encountered an error and couldn't render properly."
        extra={
            <Button type="primary" onClick={resetError}>
                Try Again
            </Button>
        }
    >
        {process.env.NODE_ENV === 'development' && (
            <Paragraph>
                <Text code>{error?.message}</Text>
            </Paragraph>
        )}
    </Result>
);

export default ErrorBoundary;