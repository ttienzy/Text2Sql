import { useEffect, Suspense, lazy } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConfigProvider, App as AntApp, Spin } from 'antd';
import { GoogleOAuthProvider } from '@react-oauth/google';
import { PrivateRoute, ConnectionGuard, ErrorBoundary } from './components';
import { MainLayout, AuthLayout } from './layouts';
import KeyboardNavigation from './components/common/KeyboardNavigation';
import { LayoutProvider } from './contexts/LayoutContext';
import useAuthStore from './store/authStore';
import { migrateStorage } from './utils/migrateStorage';

// Lazy load pages for better code splitting
const LoginPage = lazy(() => import('./pages/Login'));
const RegisterPage = lazy(() => import('./pages/Register'));
const ForgotPasswordPage = lazy(() => import('./pages/ForgotPassword'));
const ChatPage = lazy(() => import('./pages/Chat'));
const ConnectionsPage = lazy(() => import('./pages/Connections'));
const SettingsPage = lazy(() => import('./pages/Settings'));
const DbExplorerPage = lazy(() => import('./pages/DbExplorer'));
const QueryLabPage = lazy(() => import('./pages/QueryLab'));

// Loading fallback
const PageLoader = () => (
  <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>
    <Spin size="large" />
  </div>
);

// Create React Query client
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});

function App() {
  const { initializeAuth } = useAuthStore();

  // Initialize auth on app load (synchronous - reads from localStorage)
  useEffect(() => {
    // Clean up deprecated localStorage keys (one-time migration)
    migrateStorage();

    // Initialize auth from localStorage (no API call, instant)
    initializeAuth();
  }, [initializeAuth]);

  return (
    <ErrorBoundary>
      <ConfigProvider
        theme={{
          token: {
            colorPrimary: '#1890ff',
            borderRadius: 6,
          },
        }}
        notification={{
          placement: 'topRight',
          top: 24,
          bottom: 24,
          getContainer: () => document.body,
        }}
      >
        <AntApp>
          <GoogleOAuthProvider clientId={import.meta.env.VITE_GOOGLE_CLIENT_ID || ""}>
            <QueryClientProvider client={queryClient}>
              <LayoutProvider>
                <BrowserRouter>
                  <KeyboardNavigation />
                  <Suspense fallback={<PageLoader />}>
                    <Routes>
                      {/* Auth Routes */}
                      <Route element={<AuthLayout />}>
                        <Route path="/login" element={<LoginPage />} />
                        <Route path="/register" element={<RegisterPage />} />
                        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
                      </Route>

                      {/* Protected Routes */}
                      <Route element={<PrivateRoute><MainLayout /></PrivateRoute>}>
                        <Route path="/chat" element={<ConnectionGuard><ChatPage /></ConnectionGuard>} />
                        <Route path="/explorer" element={<ConnectionGuard><DbExplorerPage /></ConnectionGuard>} />
                        <Route path="/query-lab" element={<ConnectionGuard><QueryLabPage /></ConnectionGuard>} />
                        <Route path="/connections" element={<ConnectionsPage />} />
                        <Route path="/connections/new" element={<ConnectionsPage />} />
                        <Route path="/settings" element={<SettingsPage />} />

                        {/* Default redirect */}
                        <Route path="/" element={<Navigate to="/chat" replace />} />
                        <Route path="*" element={<Navigate to="/chat" replace />} />
                      </Route>

                      {/* Public fallback */}
                      <Route path="*" element={<Navigate to="/login" replace />} />
                    </Routes>
                  </Suspense>
                </BrowserRouter>
              </LayoutProvider>
            </QueryClientProvider>
          </GoogleOAuthProvider>
        </AntApp>
      </ConfigProvider>
    </ErrorBoundary>
  );
}

export default App;
