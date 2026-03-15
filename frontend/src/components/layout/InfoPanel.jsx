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
} from '@ant-design/icons';
import axiosInstance from '../../api/axios';
import useConnectionStore from '../../store/connectionStore';
import useConversationStore from '../../store/conversationStore';
import QuotaProgress from '../dashboard/QuotaProgress';

const { Text, Title } = Typography;
const { Panel } = Collapse;

const InfoPanel = ({ onSyncSchema }) => {
  const [schema, setSchema] = useState([]);
  const [isLoadingSchema, setIsLoadingSchema] = useState(false);
  
  const { activeConnection } = useConnectionStore();
  const { currentConnection: currentConv } = useConversationStore();
  
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
    <div style={{ height: '100%', display: 'flex', flexDirection: 'column', padding: 16 }}>
      <Title level={5} style={{ marginBottom: 16 }}>
        <InfoCircleOutlined style={{ marginRight: 8 }} />
        Connection Info
      </Title>
      
      {activeConnection ? (
        <>
          {/* Connection Details */}
          <div style={{ marginBottom: 24 }}>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary">Database Type:</Text>
              <div>
                <Badge 
                  status="processing" 
                  text={activeConnection.databaseType || 'Unknown'} 
                />
              </div>
            </div>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary">Host:</Text>
              <div>
                <Text ellipsis style={{ fontSize: 13 }}>
                  {activeConnection.host || 'N/A'}:{activeConnection.port || ''}
                </Text>
              </div>
            </div>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary">Database:</Text>
              <div>
                <Text>{activeConnection.database || 'N/A'}</Text>
              </div>
            </div>
            <div>
              <Text type="secondary">Status:</Text>
              <div>
                <Badge status="success" text="Connected" />
              </div>
            </div>
          </div>
          
          {/* Token Quota - Using new QuotaProgress component */}
          <div style={{ marginBottom: 24 }}>
            <QuotaProgress compact />
          </div>
          
          {/* Schema Browser */}
          <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
              <Space>
                <DatabaseOutlined />
                <Text strong>Schema Browser</Text>
              </Space>
              <Tooltip title="Sync Schema">
                <Button 
                  type="text" 
                  size="small" 
                  icon={<SyncOutlined spin={isLoadingSchema} />} 
                  onClick={handleSyncSchema}
                  loading={isLoadingSchema}
                />
              </Tooltip>
            </div>
            
            {isLoadingSchema ? (
              <div style={{ textAlign: 'center', padding: 24 }}>
                <Spin />
              </div>
            ) : schema.length === 0 ? (
              <Empty 
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="No schema available"
                style={{ padding: 24 }}
              />
            ) : (
              <Collapse 
                ghost 
                defaultActiveKey={schema.slice(0, 3).map(t => t.name)}
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
          {currentConv && (
            <div style={{ marginTop: 16, paddingTop: 16, borderTop: '1px solid #f0f0f0' }}>
              <Text type="secondary" style={{ fontSize: 12 }}>
                Conversation: {currentConv.title || 'Untitled'}
              </Text>
              <br />
              <Text type="secondary" style={{ fontSize: 12 }}>
                Created: {currentConv.createdAt 
                  ? new Date(currentConv.createdAt).toLocaleDateString() 
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
