import { Navigate, useLocation } from 'react-router-dom';
import useAuthStore from '../store/authStore';

/**
 * PrivateRoute - Protects routes that require authentication
 * Redirects to login if user is not authenticated
 * 
 * Note: We check localStorage directly for faster initial render
 * This avoids race conditions with store initialization
 */
const PrivateRoute = ({ children }) => {
  const { isAuthenticated } = useAuthStore();
  const location = useLocation();
  
  // Check localStorage directly for immediate auth state
  const hasToken = localStorage.getItem('tts_access_token') !== null;
  const isAuth = isAuthenticated || hasToken;
  
  // Redirect to login if not authenticated
  if (!isAuth) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }
  
  return children;
};

export default PrivateRoute;
