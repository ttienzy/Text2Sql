import { useState, useCallback } from 'react';
import { Layout, message } from 'antd';
import Sidebar from '../components/layout/Sidebar';
import ChatArea from '../components/layout/ChatArea';
import InfoPanel from '../components/layout/InfoPanel';
import useConnectionStore from '../store/connectionStore';
import useConversationStore from '../store/conversationStore';
import { 
  useCreateConversationMutation,
} from '../api/conversations';

const { Sider, Content } = Layout;

// Default widths for the 3-column layout
const SIDEBAR_WIDTH = 280;
const INFOPANEL_WIDTH = 320;

const ChatLayout = () => {
  const [sidebarVisible] = useState(true);
  const [infoPanelVisible] = useState(true);
  
  const { activeConnection } = useConnectionStore();
  const { 
    setCurrentConversation,
    addConversation,
  } = useConversationStore();
  
  // React Query mutations
  const createConversationMutation = useCreateConversationMutation({
    onSuccess: (data) => {
      addConversation(data);
      message.success('New conversation created');
    },
    onError: (error) => {
      const errorMsg = error.response?.data?.message || error.message || 'Failed to create conversation';
      message.error(errorMsg);
    },
  });
  
  // Note: Connection fetching is handled by MainLayout - no need to fetch here
  
  // Handle new conversation creation
  const handleNewConversation = useCallback(async () => {
    if (!activeConnection?.id) {
      message.warning('Please select a connection first');
      return;
    }
    
    try {
      const newConversation = await createConversationMutation.mutateAsync({
        connectionId: activeConnection.id,
        title: 'New Conversation',
      });
      
      setCurrentConversation(newConversation);
    } catch (error) {
      console.error('Failed to create new conversation:', error);
    }
  }, [activeConnection, createConversationMutation, setCurrentConversation]);
  
  // Handle conversation selection
  const handleConversationSelect = useCallback((conversation) => {
    setCurrentConversation(conversation);
  }, [setCurrentConversation]);
  
  // Handle sync schema
  const handleSyncSchema = useCallback(async () => {
    // This triggers a re-fetch of schema in InfoPanel
    // Schema will be refreshed automatically
  }, []);
  
  return (
    <Layout style={{ height: 'calc(100vh - 64px)', background: '#fff' }}>
      {/* Left Sidebar */}
      {sidebarVisible && (
        <Sider
          width={SIDEBAR_WIDTH}
          theme="light"
          style={{ 
            borderRight: '1px solid #f0f0f0',
            overflow: 'hidden',
          }}
        >
          <Sidebar 
            onConversationSelect={handleConversationSelect}
            onNewConversation={handleNewConversation}
          />
        </Sider>
      )}
      
      {/* Main Chat Area */}
      <Content style={{ 
        display: 'flex', 
        flexDirection: 'column',
        overflow: 'hidden',
      }}>
        <ChatArea />
      </Content>
      
      {/* Right Info Panel */}
      {infoPanelVisible && (
        <Sider
          width={INFOPANEL_WIDTH}
          theme="light"
          style={{ 
            borderLeft: '1px solid #f0f0f0',
            overflow: 'hidden',
          }}
        >
          <InfoPanel onSyncSchema={handleSyncSchema} />
        </Sider>
      )}
    </Layout>
  );
};

export default ChatLayout;
