import { useState, useEffect } from 'react';
import {
  Typography,
  Space,
  Badge,
  List,
  Collapse,
  Button,
  Tooltip,
  Spin,
  Empty,
} from 'antd';
import {
  DatabaseOutlined,
  TableOutlined,
  ColumnWidthOutlined,
  SyncOutlined,
  InfoCircleOutlined,
  HistoryOutlined,
} from '@ant-design/icons';
import { SchemaBrowserSkeleton } from '../common';
import { ConversationContext } from '../chat';
import axiosInstance from '../../api/axios';
import { useRecentQueriesQuery } from '../../api/messages/queries';
import useConnectionStore from '../../store/connectionStore';
import useConversationStore from '../../store/conversationStore';
import QuotaProgress from '../dashboard/QuotaProgress';

const { Text, Title } = Typography;
const { Panel } = Collapse;

const InfoPanel = ({ onSyncSchema }) => {
  const [schema, setSchema] = useState([]);
  const [isLoadingSchema, setIsLoadingSchema] = useState(false);

  const { activeConnection } = useConnectionStore();
  const { currentConversation } = useConversationStore();

  // Fetch recent queries
  const { data: recentQueries, isLoading: isLoadingQueries } = useRecentQueriesQuery();

  // Fetch schema when connection changes
  useEffect(() => {
    if (activeConnection?.id) {
      fetchSchema(activeConnection.id);
    } else {
      setSchema([]);
    }
  }, [activeConnection?.id]);

  const fetchSchema = async (connectionId) => {
    setIsLoadingSchema(true);
    try {
      const response = await axiosInstance.get(`/api/connections/${connectionId}/schema`);
      setSchema(response.data.tables || []);
    } catch (error) {
      console.error('Failed to fetch schema:', error);
      setSchema([]);
    } finally {
      setIsLoadingSchema(false);
    }
  };

  const handleSyncSchema = async () => {
    if (!activeConnection?.id || isLoadingSchema) return;

    try {
      await axiosInstance.post(`/api/connections/${activeConnection.id}/sync`);
      // Refresh schema after sync
      await fetchSchema(activeConnection.id);
      if (onSyncSchema) {
        onSyncSchema();
      }
    } catch (error) {
      console.error('Failed to sync schema:', error);
    }
  };

  return (
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column', padding: 12 }}>
      <Title level={5} style={{ marginBottom: 12, fontSize: 14 }}>
        <InfoCircleOutlined style={{ marginRight: 6 }} />
        Connection Info
      </Title>

      {activeConnection ? (
        <>
          {/* Connection Details */}
          <div style={{ marginBottom: 16 }}>
            <div style={{ marginBottom: 6 }}>
              <Text type="secondary" style={{ fontSize: 11 }}>Database Type:</Text>
              <div>
                <Badge
                  status="processing"
                  text={activeConnection.provider?.toUpperCase() || 'Unknown'}
                />
              </div>
            </div>
            <div style={{ marginBottom: 6 }}>
              <Text type="secondary" style={{ fontSize: 11 }}>Host:</Text>
              <div>
                <Text ellipsis style={{ fontSize: 12 }}>
                  {activeConnection.host || 'N/A'}:{activeConnection.port || ''}
                </Text>
              </div>
            </div>
            <div style={{ marginBottom: 6 }}>
              <Text type="secondary" style={{ fontSize: 11 }}>Database:</Text>
              <div>
                <Text style={{ fontSize: 12 }}>{activeConnection.database || 'N/A'}</Text>
              </div>
            </div>
            <div style={{ marginBottom: 6 }}>
              <Text type="secondary" style={{ fontSize: 11 }}>Status:</Text>
              <div>
                <Badge
                  status={activeConnection.isConnected ? "success" : "error"}
                  text={activeConnection.isConnected ? "Connected" : "Disconnected"}
                />
              </div>
            </div>
            <div>
              <Text type="secondary" style={{ fontSize: 11 }}>Schema Status:</Text>
              <div>
                <Badge
                  status={activeConnection.schemaSync?.isSynced ? "success" : "warning"}
                  text={activeConnection.schemaSync?.isSynced
                    ? `${activeConnection.schemaSync.tableCount || 0} tables indexed`
                    : "Not indexed"
                  }
                />
              </div>
            </div>
          </div>

          {/* Token Quota - Using new QuotaProgress component */}
          <div style={{ marginBottom: 16 }}>
            <QuotaProgress compact />
          </div>

          {/* Conversation Analytics - Show when conversation is active */}
          {currentConversation && (
            <ConversationContext
              conversationId={currentConversation.id}
              visible={true}
            />
          )}

          {/* Recent Queries */}
          <div style={{ marginBottom: 16 }}>
            <div style={{ display: 'flex', alignItems: 'center', marginBottom: 6 }}>
              <HistoryOutlined style={{ marginRight: 6, color: '#1890ff', fontSize: 12 }} />
              <Text strong style={{ fontSize: 12 }}>Recent Queries</Text>
            </div>
            {isLoadingQueries ? (
              <Spin size="small" />
            ) : recentQueries && recentQueries.length > 0 ? (
              <div style={{ fontSize: 11 }}>
                {recentQueries.map((query, index) => (
                  <div key={query.id} style={{ marginBottom: 4, padding: 4, backgroundColor: '#f9f9f9', borderRadius: 3 }}>
                    <Text style={{ fontSize: 10, color: '#666', fontFamily: 'monospace' }}>
                      {query.sqlQuery || query.content}
                    </Text>
                    <div style={{ marginTop: 1 }}>
                      <Text type="secondary" style={{ fontSize: 9 }}>
                        {new Date(query.createdAt).toLocaleTimeString()}
                      </Text>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <Text type="secondary" style={{ fontSize: 11 }}>
                No recent queries
              </Text>
            )}
          </div>

          {/* Schema Browser */}
          <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
              <Space size="small">
                <DatabaseOutlined style={{ fontSize: 12 }} />
                <Text strong style={{ fontSize: 12 }}>Schema Browser</Text>
              </Space>
              <Tooltip title="Sync Schema">
                <Button
                  type="text"
                  size="small"
                  icon={<SyncOutlined spin={isLoadingSchema} style={{ fontSize: 11 }} />}
                  onClick={handleSyncSchema}
                  loading={isLoadingSchema}
                />
              </Tooltip>
            </div>

            {isLoadingSchema ? (
              <SchemaBrowserSkeleton count={3} />
            ) : schema.length === 0 ? (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="No schema available"
                style={{ padding: 16 }}
              />
            ) : (
              <Collapse
                ghost
                size="small"
                defaultActiveKey={schema.slice(0, 2).map(t => t.name)}
                style={{ flex: 1, overflow: 'auto' }}
              >
                {schema.map((table) => (
                  <Panel
                    key={table.name}
                    header={
                      <Space>
                        <TableOutlined />
                        <Text style={{ fontWeight: 500 }}>{table.name}</Text>
                        <Text type="secondary" style={{ fontSize: 12 }}>
                          ({table.columns?.length || 0})
                        </Text>
                      </Space>
                    }
                  >
                    <List
                      size="small"
                      dataSource={table.columns || []}
                      renderItem={(column) => (
                        <List.Item style={{ padding: '4px 0', border: 'none' }}>
                          <Space>
                            <ColumnWidthOutlined style={{ color: '#8c8c8c' }} />
                            <Text style={{ fontSize: 13 }}>{column.name}</Text>
                            <Text type="secondary" style={{ fontSize: 12 }}>
                              {column.type}
                            </Text>
                            {column.isPrimaryKey && (
                              <Badge status="warning" text="PK" />
                            )}
                            {column.isForeignKey && (
                              <Badge status="blue" text="FK" />
                            )}
                          </Space>
                        </List.Item>
                      )}
                    />
                  </Panel>
                ))}
              </Collapse>
            )}
          </div>

          {/* Conversation Metadata */}
          {currentConversation && (
            <div style={{ marginTop: 16, paddingTop: 16, borderTop: '1px solid #f0f0f0' }}>
              <Text type="secondary" style={{ fontSize: 12 }}>
                Conversation: {currentConversation.title || 'Untitled'}
              </Text>
              <br />
              <Text type="secondary" style={{ fontSize: 12 }}>
                Created: {currentConversation.createdAt
                  ? new Date(currentConversation.createdAt).toLocaleDateString()
                  : 'N/A'}
              </Text>
            </div>
          )}
        </>
      ) : (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description="No connection selected"
          style={{ marginTop: 48 }}
        />
      )}
    </div>
  );
};

export default InfoPanel;
