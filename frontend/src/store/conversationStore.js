/**
 * Conversation Store - Zustand
 * Manages conversation state with limited localStorage persistence
 */
import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { handleQuotaExceeded, clearNonEssentialStorage } from '../utils/storageUtils';

// SessionStorage key for active conversation
const SESSION_ACTIVE_CONVERSATION_KEY = 'activeConversationId';

/**
 * Create the conversation store with selective persistence
 * Only persist essential data to avoid localStorage quota issues
 */
const useConversationStore = create(
  persist(
    (set, get) => ({
      // State
      conversations: [],
      currentConversation: null,
      messages: [],
      isLoading: false,
      isSending: false,
      error: null,

      // Connection ID for current conversation list
      currentConnectionId: null,

      // Actions
      setConversations: (conversations) => set({ conversations }),

      setCurrentConnectionId: (connectionId) => set({ currentConnectionId: connectionId }),

      setCurrentConversation: (conversation) => {
        // Store current conversation ID in sessionStorage for persistence
        if (conversation?.id) {
          sessionStorage.setItem(SESSION_ACTIVE_CONVERSATION_KEY, conversation.id);
        } else {
          sessionStorage.removeItem(SESSION_ACTIVE_CONVERSATION_KEY);
        }
        set({ currentConversation: conversation });
      },

      setMessages: (messages) => set({ messages }),

      appendMessage: (message) => {
        set((state) => ({
          messages: [...state.messages, message],
        }));
      },

      // Update conversation in the list (e.g., after title change)
      updateConversationInList: (id, updates) => {
        set((state) => ({
          conversations: state.conversations.map((c) =>
            c.id === id ? { ...c, ...updates } : c
          ),
          currentConversation:
            state.currentConversation?.id === id
              ? { ...state.currentConversation, ...updates }
              : state.currentConversation,
        }));
      },

      // Remove conversation from the list
      removeConversation: (id) => {
        set((state) => {
          const newConversations = state.conversations.filter((c) => c.id !== id);
          const newCurrentConversation =
            state.currentConversation?.id === id
              ? newConversations.length > 0
                ? newConversations[0]
                : null
              : state.currentConversation;

          if (!newCurrentConversation) {
            sessionStorage.removeItem(SESSION_ACTIVE_CONVERSATION_KEY);
          }

          return {
            conversations: newConversations,
            currentConversation: newCurrentConversation,
            messages: newCurrentConversation ? state.messages : [],
          };
        });
      },

      // Add new conversation to the list
      addConversation: (conversation) => {
        set((state) => ({
          conversations: [conversation, ...state.conversations],
          currentConversation: conversation,
          messages: [],
        }));
      },

      // Clear error
      clearError: () => set({ error: null }),

      // Set loading state
      setLoading: (isLoading) => set({ isLoading }),

      // Set sending state
      setSending: (isSending) => set({ isSending }),

      // Set error
      setError: (error) => set({ error }),

      // Clear all conversations
      clearConversations: () => {
        sessionStorage.removeItem(SESSION_ACTIVE_CONVERSATION_KEY);
        set({
          conversations: [],
          currentConversation: null,
          messages: [],
          currentConnectionId: null,
        });
      },

      // Restore active conversation from sessionStorage after connection changes
      restoreActiveConversation: () => {
        const { conversations } = get();
        const lastConversationId = sessionStorage.getItem(SESSION_ACTIVE_CONVERSATION_KEY);

        if (lastConversationId && conversations.length > 0) {
          const conversation = conversations.find((c) => c.id === lastConversationId);
          if (conversation) {
            set({ currentConversation: conversation });
            return conversation;
          }
        }
        return null;
      },

      // Clear localStorage data (for quota issues)
      clearStorageData: () => {
        try {
          // Clear conversation store from localStorage
          localStorage.removeItem('conversation-store');
          sessionStorage.removeItem(SESSION_ACTIVE_CONVERSATION_KEY);

          // Clear other non-essential storage
          clearNonEssentialStorage();

          // Reset state
          set({
            conversations: [],
            currentConversation: null,
            messages: [],
            currentConnectionId: null,
            error: null,
            isLoading: false,
            isSending: false,
          });

          console.log('✅ Cleared conversation storage data');
        } catch (error) {
          console.error('❌ Error clearing storage data:', error);
        }
      },
    }),
    {
      name: 'conversation-store',
      // Only persist essential data to avoid quota issues
      partialize: (state) => ({
        currentConnectionId: state.currentConnectionId,
        // Don't persist conversations, messages, or other large data
      }),
      // Custom storage with quota handling
      storage: {
        getItem: (name) => {
          try {
            const item = localStorage.getItem(name);
            return item ? JSON.parse(item) : null;
          } catch (error) {
            console.error('❌ Error reading from localStorage:', error);
            return null;
          }
        },
        setItem: (name, value) => {
          return handleQuotaExceeded(() => {
            localStorage.setItem(name, JSON.stringify(value));
          });
        },
        removeItem: (name) => {
          try {
            localStorage.removeItem(name);
          } catch (error) {
            console.error('❌ Error removing from localStorage:', error);
          }
        },
      },
      // Add error handling for quota exceeded
      onRehydrateStorage: () => (state, error) => {
        if (error) {
          console.error('❌ Error rehydrating conversation store:', error);
          // Clear storage if there's an error
          try {
            localStorage.removeItem('conversation-store');
            clearNonEssentialStorage();
          } catch (e) {
            console.error('❌ Error clearing conversation store:', e);
          }
        }
      },
    },
  )
);

export default useConversationStore;
