import { useState, useEffect } from 'react';
import {
  List,
  Input,
  Button,
  Space,
  Typography,
  Tabs,
  Badge,
  Avatar,
  Dropdown,
  Empty,
  Spin,
  Tooltip,
} from 'antd';
import {
  PlusOutlined,
  SearchOutlined,
  DatabaseOutlined,
  MessageOutlined,
  MoreOutlined,
  DeleteOutlined,
  EditOutlined,
  HolderOutlined,
} from '@ant-design/icons';
import { ResponsiveConversationListSkeleton } from '../common';
import { useConversationsQuery } from '../../api/conversations';
import useConnectionStore from '../../store/connectionStore';
import useConversationStore from '../../store/conversationStore';

const { Text, Title } = Typography;

const Sidebar = ({ onConversationSelect, onNewConversation }) => {
  const [searchQuery, setSearchQuery] = useState('');
  const [activeTab, setActiveTab] = useState('conversations');

  const { activeConnection, connections, setActiveConnection } = useConnectionStore();
  const {
    conversations,
    currentConversation,
    setConversations,
    setCurrentConversation,
    setCurrentConnectionId,
  } = useConversationStore();

  // Use React Query for conversations
  const { data: fetchedConversations, isLoading } = useConversationsQuery(
    activeConnection?.id,
    {
      enabled: !!activeConnection?.id,
    }
  );

  // Update conversations when fetched
  useEffect(() => {
    if (fetchedConversations) {
      setConversations(fetchedConversations);
    }
  }, [fetchedConversations, setConversations]);

  // Update connection ID when connection changes
  useEffect(() => {
    if (activeConnection?.id) {
      setCurrentConnectionId(activeConnection.id);
    }
  }, [activeConnection?.id, setCurrentConnectionId]);

  // Filter conversations by search query
  const filteredConversations = conversations.filter((c) =>
    c.title?.toLowerCase().includes(searchQuery.toLowerCase())
  );

  const handleConversationClick = (conversation) => {
    setCurrentConversation(conversation);
    if (onConversationSelect) {
      onConversationSelect(conversation);
    }
  };

  const handleNewConversation = () => {
    if (onNewConversation) {
      onNewConversation();
    }
  };

  const handleConnectionSelect = (connection) => {
    setActiveConnection(connection);
    setCurrentConversation(null);
  };

  // Menu items for conversation actions
  const getConversationMenuItems = () => [
    {
      key: 'rename',
      icon: <EditOutlined />,
      label: 'Rename',
      onClick: () => {
        // TODO: Implement rename functionality
      },
    },
    {
      key: 'delete',
      icon: <DeleteOutlined />,
      label: 'Delete',
      danger: true,
      onClick: () => {
        // TODO: Implement delete functionality
      },
    },
  ];

  const tabItems = [
    {
      key: 'conversations',
      label: (
        <span>
          <MessageOutlined /> Conversations
        </span>
      ),
      children: (
        <div style={{ padding: '8px 0' }}>
          {/* Search */}
          <div style={{ padding: '0 8px', marginBottom: 8 }}>
            <Input
              prefix={<SearchOutlined />}
              placeholder="Search conversations..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              allowClear
            />
          </div>

          {/* New Conversation Button */}
          <div style={{ padding: '0 8px', marginBottom: 8 }}>
            <Button
              type="primary"
              icon={<PlusOutlined />}
              onClick={handleNewConversation}
              block
              disabled={!activeConnection}
            >
              New Conversation
            </Button>
          </div>

          {/* Conversations List */}
          {isLoading ? (
            <ResponsiveConversationListSkeleton />
          ) : !activeConnection ? (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description="Select a connection first"
              style={{ padding: 24 }}
            />
          ) : filteredConversations.length === 0 ? (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={searchQuery ? 'No conversations found' : 'No conversations yet'}
              style={{ padding: 24 }}
            />
          ) : (
            <List
              dataSource={filteredConversations}
              renderItem={(conversation) => (
                <List.Item
                  style={{
                    cursor: 'pointer',
                    padding: '8px 12px',
                    backgroundColor: currentConversation?.id === conversation.id ? '#e6f7ff' : 'transparent',
                    borderLeft: currentConversation?.id === conversation.id ? '3px solid #1890ff' : '3px solid transparent',
                  }}
                  onClick={() => handleConversationClick(conversation)}
                >
                  <div style={{ flex: 1, minWidth: 0, overflow: 'hidden' }}>
                    <Text
                      ellipsis
                      style={{
                        display: 'block',
                        fontWeight: currentConversation?.id === conversation.id ? 600 : 400,
                        maxWidth: '100%',
                      }}
                    >
                      {conversation.title || 'Untitled'}
                    </Text>
                    <Text
                      type="secondary"
                      style={{
                        fontSize: 12,
                        display: 'block',
                        overflow: 'hidden',
                        whiteSpace: 'nowrap',
                        textOverflow: 'ellipsis',
                        maxWidth: '100%',
                      }}
                    >
                      {conversation.messageCount ?? 0} messages
                      {conversation.lastQuery ? ` · ${conversation.lastQuery}` : ''}
                    </Text>
                  </div>
                  <Dropdown
                    menu={{
                      items: getConversationMenuItems(conversation),
                      onClick: ({ key }) => {
                        if (key === 'delete') {
                          // Handle delete
                        } else if (key === 'rename') {
                          // Handle rename
                        }
                      }
                    }}
                    trigger={['click']}
                  >
                    <Button
                      type="text"
                      size="small"
                      icon={<MoreOutlined />}
                      onClick={(e) => e.stopPropagation()}
                    />
                  </Dropdown>
                </List.Item>
              )}
            />
          )}
        </div>
      ),
    },
    {
      key: 'connections',
      label: (
        <span>
          <DatabaseOutlined /> Connections
        </span>
      ),
      children: (
        <div style={{ padding: '8px 0' }}>
          <List
            dataSource={connections}
            renderItem={(connection) => (
              <List.Item
                style={{
                  cursor: 'pointer',
                  padding: '8px 12px',
                  backgroundColor: activeConnection?.id === connection.id ? '#e6f7ff' : 'transparent',
                  borderLeft: activeConnection?.id === connection.id ? '3px solid #1890ff' : '3px solid transparent',
                }}
                onClick={() => handleConnectionSelect(connection)}
              >
                <Space>
                  <Avatar
                    size="small"
                    icon={<DatabaseOutlined />}
                    style={{
                      backgroundColor: activeConnection?.id === connection.id ? '#1890ff' : '#8c8c8c'
                    }}
                  />
                  <div>
                    <Text ellipsis style={{ display: 'block', maxWidth: 160 }}>
                      {connection.name}
                    </Text>
                    <Text type="secondary" style={{ fontSize: 12 }}>
                      {connection.databaseType || 'Database'}
                    </Text>
                  </div>
                </Space>
                {connection.isActive && (
                  <Badge status="success" style={{ marginLeft: 8 }} />
                )}
              </List.Item>
            )}
          />
        </div>
      ),
    },
  ];

  return (
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
      {/* Header */}
      <div style={{ padding: '12px 16px', borderBottom: '1px solid #f0f0f0' }}>
        <Title level={5} style={{ margin: 0 }}>
          {activeConnection?.name || 'Select a Connection'}
        </Title>
      </div>

      {/* Tabs */}
      <Tabs
        activeKey={activeTab}
        onChange={setActiveTab}
        items={tabItems}
        style={{ flex: 1, overflow: 'hidden' }}
        tabBarStyle={{ padding: '0 8px', marginBottom: 0 }}
      />
    </div>
  );
};

export default Sidebar;
