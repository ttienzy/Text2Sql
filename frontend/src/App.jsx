import { useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConfigProvider } from 'antd';
import { PrivateRoute, ConnectionGuard, ErrorBoundary } from './components';
import { MainLayout, AuthLayout } from './layouts';
import { LoginPage, RegisterPage, ChatPage, ConnectionsPage, SettingsPage } from './pages';
import { LayoutProvider } from './contexts/LayoutContext';
import useAuthStore from './store/authStore';

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

  // Initialize auth on app load
  useEffect(() => {
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
      >
        <QueryClientProvider client={queryClient}>
          <LayoutProvider>
            <BrowserRouter>
              <Routes>
                {/* Auth Routes */}
                <Route element={<AuthLayout />}>
                  <Route path="/login" element={<LoginPage />} />
                  <Route path="/register" element={<RegisterPage />} />
                </Route>

                {/* Protected Routes */}
                <Route element={<PrivateRoute><MainLayout /></PrivateRoute>}>
                  <Route path="/chat" element={<ConnectionGuard><ChatPage /></ConnectionGuard>} />
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
            </BrowserRouter>
          </LayoutProvider>
        </QueryClientProvider>
      </ConfigProvider>
    </ErrorBoundary>
  );
}

export default App;
