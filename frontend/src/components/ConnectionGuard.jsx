import { Navigate } from 'react-router-dom';
import { Spin } from 'antd';
import useConnectionStore from '../store/connectionStore';

/**
 * ConnectionGuard - Protects routes that require an active database connection
 * Redirects to connections page if no connection is selected
 */
const ConnectionGuard = ({ children }) => {
  const { activeConnection, isLoading, connections } = useConnectionStore();
  
  // Show loading spinner while fetching connections
  if (isLoading) {
    return (
      <div style={{ 
        display: 'flex', 
        justifyContent: 'center', 
        alignItems: 'center', 
        height: '100vh' 
      }}>
        <Spin size="large" tip="Loading connections..." />
      </div>
    );
  }
  
  // Only redirect after loading is complete
  if (!activeConnection) {
    // If no connections exist at all after loading, redirect to create connection
    if (!connections || connections.length === 0) {
      return <Navigate to="/connections/new" replace />;
    }
    // If connections exist but none selected, redirect to connections list
    return <Navigate to="/connections" replace />;
  }
  
  return children;
};

export default ConnectionGuard;
