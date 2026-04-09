import { Navigate, useLocation } from 'react-router-dom';
import useAuthStore from '../store/authStore';

/**
 * PrivateRoute - Protects routes that require authentication
 * Redirects to login if user is not authenticated
 * 
 * Note: We check refreshToken in localStorage for faster initial render
 * This avoids race conditions with store initialization
 */
const PrivateRoute = ({ children }) => {
  const { isAuthenticated } = useAuthStore();
  const location = useLocation();

  // Check refreshToken in localStorage for immediate auth state
  // (accessToken is memory-only, but refreshToken persists)
  const hasRefreshToken = localStorage.getItem('tts_refresh_token') !== null;
  const isAuth = isAuthenticated || hasRefreshToken;

  // Redirect to login if not authenticated
  if (!isAuth) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  return children;
};

export default PrivateRoute;
