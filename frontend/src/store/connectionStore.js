/**
 * Connection Store - Zustand
 * Manages connection state with sessionStorage for active connection persistence
 */
import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import axiosInstance from '../api/axios';

// SessionStorage key for active connection
const SESSION_ACTIVE_CONNECTION_KEY = 'activeConnectionId';

const useConnectionStore = create(
  persist(
    (set, get) => ({
      // State
      connections: [],
      activeConnection: null,
      isLoading: false,
      isTesting: false,
      isSyncing: false,
      error: null,
      testResult: null,
      syncResult: null,
      isInitialized: false, // Flag to track if connections have been fetched

      // Actions
      setConnections: (connections) => set({ connections }),

      setActiveConnection: (connection) => {
        // Store active connection ID in sessionStorage (not localStorage)
        if (connection?.id) {
          sessionStorage.setItem(SESSION_ACTIVE_CONNECTION_KEY, connection.id);

          // ✅ P1: Auto-load schema when selecting connection
          const state = get();
          if (connection && !connection.schemaLoaded) {
            // Trigger schema load in background (non-blocking)
            state.checkSchemaStatus(connection.id).catch(err => {
              console.warn('[ConnectionStore] Failed to check schema status:', err);
            });
          }
        } else {
          sessionStorage.removeItem(SESSION_ACTIVE_CONNECTION_KEY);
        }
        set({ activeConnection: connection });
      },

      fetchConnections: async () => {
        const state = get();
        // Prevent duplicate fetches - only fetch once
        if (state.isInitialized && state.connections.length > 0) {
          return state.connections;
        }

        set({ isLoading: true, error: null });
        try {
          const response = await axiosInstance.get('/api/connections');
          const connections = response.data;

          // Restore last selected connection from sessionStorage
          const lastConnectionId = sessionStorage.getItem(SESSION_ACTIVE_CONNECTION_KEY);
          const activeConnection = lastConnectionId
            ? connections.find((c) => c.id === lastConnectionId)
            : null;

          set({
            connections,
            activeConnection: activeConnection || (connections.length > 0 ? connections[0] : null),
            isLoading: false,
            isInitialized: true, // Mark as initialized
          });

          return connections;
        } catch (error) {
          set({
            isLoading: false,
            error: error.response?.data?.message || 'Failed to fetch connections',
            isInitialized: true, // Mark as initialized even on error to prevent retry loops
          });
          throw error;
        }
      },

      createConnection: async (connectionData) => {
        set({ isLoading: true, error: null });
        try {
          const response = await axiosInstance.post('/api/connections', connectionData);
          const newConnection = response.data;

          set((state) => ({
            connections: [...state.connections, newConnection],
            activeConnection: newConnection,
            isLoading: false,
          }));

          return newConnection;
        } catch (error) {
          set({
            isLoading: false,
            error: error.response?.data?.message || 'Failed to create connection',
          });
          throw error;
        }
      },

      updateConnection: async (id, connectionData) => {
        set({ isLoading: true, error: null });
        try {
          const response = await axiosInstance.put(`/api/connections/${id}`, connectionData);
          const updatedConnection = response.data;

          set((state) => ({
            connections: state.connections.map((c) =>
              c.id === id ? updatedConnection : c
            ),
            activeConnection:
              state.activeConnection?.id === id
                ? updatedConnection
                : state.activeConnection,
            isLoading: false,
          }));

          return updatedConnection;
        } catch (error) {
          set({
            isLoading: false,
            error: error.response?.data?.message || 'Failed to update connection',
          });
          throw error;
        }
      },

      deleteConnection: async (id) => {
        set({ isLoading: true, error: null });
        try {
          await axiosInstance.delete(`/api/connections/${id}`);

          set((state) => {
            const newConnections = state.connections.filter((c) => c.id !== id);
            const newActiveConnection =
              state.activeConnection?.id === id
                ? newConnections.length > 0
                  ? newConnections[0]
                  : null
                : state.activeConnection;

            if (!newActiveConnection) {
              sessionStorage.removeItem(SESSION_ACTIVE_CONNECTION_KEY);
            }

            return {
              connections: newConnections,
              activeConnection: newActiveConnection,
              isLoading: false,
            };
          });
        } catch (error) {
          set({
            isLoading: false,
            error: error.response?.data?.message || 'Failed to delete connection',
          });
          throw error;
        }
      },

      testConnection: async (connectionData) => {
        set({ isTesting: true, testResult: null, error: null });
        try {
          // If connectionData has an ID, test existing connection
          // Otherwise, test the provided data (for new connections)
          const url = connectionData.id
            ? `/api/connections/${connectionData.id}/test`
            : '/api/connections/test';

          const response = await axiosInstance.post(url, connectionData);
          set({ testResult: response.data, isTesting: false });
          return response.data;
        } catch (error) {
          set({
            isTesting: false,
            error: error.response?.data?.message || 'Failed to test connection',
            testResult: error.response?.data || error,
          });
          throw error.response?.data || error;
        }
      },

      syncSchema: async (connectionId) => {
        set({ isSyncing: true, syncResult: null, error: null });
        try {
          const response = await axiosInstance.post(`/api/connections/${connectionId}/sync`);
          const syncResult = response.data;

          set((state) => ({
            syncResult,
            isSyncing: false,
            // Update the connection with the new sync status
            connections: state.connections.map((c) =>
              c.id === connectionId
                ? { ...c, schemaSync: syncResult }
                : c
            ),
            activeConnection:
              state.activeConnection?.id === connectionId
                ? { ...state.activeConnection, schemaSync: syncResult }
                : state.activeConnection,
          }));

          return syncResult;
        } catch (error) {
          set({
            isSyncing: false,
            error: error.response?.data?.message || 'Failed to sync schema',
          });
          throw error;
        }
      },

      clearError: () => set({ error: null }),

      clearTestResult: () => set({ testResult: null }),

      clearSyncResult: () => set({ syncResult: null }),

      // ✅ P1: Check schema status for a connection
      checkSchemaStatus: async (connectionId) => {
        try {
          const response = await axiosInstance.get(`/api/connections/${connectionId}/schema/status`);
          const { schemaLoaded, tableCount } = response.data;

          // Update connection with schema status
          set((state) => ({
            connections: state.connections.map((c) =>
              c.id === connectionId
                ? { ...c, schemaLoaded, tableCount }
                : c
            ),
            activeConnection:
              state.activeConnection?.id === connectionId
                ? { ...state.activeConnection, schemaLoaded, tableCount }
                : state.activeConnection,
          }));

          // If schema not loaded, auto-test connection to load it
          if (!schemaLoaded) {
            console.log('[ConnectionStore] Schema not loaded, auto-testing connection...');
            await get().testConnection({ id: connectionId });
          }

          return { schemaLoaded, tableCount };
        } catch (error) {
          console.warn('[ConnectionStore] Failed to check schema status:', error);
          throw error;
        }
      },

      // Initialize active connection from sessionStorage on load
      initializeFromSession: () => {
        const { connections, activeConnection } = get();
        const lastConnectionId = sessionStorage.getItem(SESSION_ACTIVE_CONNECTION_KEY);

        if (lastConnectionId && connections.length > 0 && !activeConnection) {
          const connection = connections.find((c) => c.id === lastConnectionId);
          if (connection) {
            set({ activeConnection: connection });
          }
        }
      },
    }),
    {
      name: 'connection-store',
      partialize: (state) => ({
        // Only persist these fields
        connections: state.connections,
        // Note: activeConnection is stored in sessionStorage, not persisted here
      }),
    }
  )
);

export default useConnectionStore;
