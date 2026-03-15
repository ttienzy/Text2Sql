/**
 * ConnectionCard - Card component for displaying connection information
 */
import { useState } from 'react';
import {
  Card,
  Tag,
  Space,
  Button,
  Typography,
  Dropdown,
  Modal,
  Tooltip,
} from 'antd';
import {
  EditOutlined,
  DeleteOutlined,
  PlayCircleOutlined,
  SyncOutlined,
  MoreOutlined,
  DatabaseOutlined,
  CheckCircleOutlined,
  CloseCircleOutlined,
  ClockCircleOutlined,
  SettingOutlined,
} from '@ant-design/icons';
import dayjs from 'dayjs';
import relativeTime from 'dayjs/plugin/relativeTime';

dayjs.extend(relativeTime);

const { Text, Title } = Typography;

/**
 * Database provider icons/colors
 */
const PROVIDER_CONFIG = {
  SqlServer: { color: '#CC2927', label: 'SQL Server' },
  PostgreSql: { color: '#336791', label: 'PostgreSQL' },
  MySql: { color: '#4479A1', label: 'MySQL' },
  Sqlite: { color: '#003B57', label: 'SQLite' },
};

/**
 * ConnectionCard Component
 * @param {Object} props
 * @param {Object} props.connection - Connection data
 * @param {boolean} props.isActive - Whether this is the active connection
 * @param {Function} props.onConnect - Connect to this connection
 * @param {Function} props.onEdit - Edit this connection
 * @param {Function} props.onDelete - Delete this connection
 * @param {Function} props.onTest - Test this connection
 * @param {Function} props.onSync - Sync schema for this connection
 * @param {boolean} props.isTesting - Whether test is in progress
 * @param {boolean} props.isSyncing - Whether sync is in progress
 */
const ConnectionCard = ({
  connection,
  isActive = false,
  onConnect,
  onEdit,
  onDelete,
  onTest,
  onSync,
  isTesting = false,
  isSyncing = false,
}) => {
  const [deleteModalVisible, setDeleteModalVisible] = useState(false);

  const provider = PROVIDER_CONFIG[connection.provider] || {
    color: '#1890ff',
    label: connection.provider,
  };

  // Format last used time
  const formatLastUsed = () => {
    if (!connection.lastUsedAt) return 'Never';
    return dayjs(connection.lastUsedAt).fromNow();
  };

  // Format last synced time
  const formatLastSynced = () => {
    if (!connection.schemaSync?.lastSyncedAt) return 'Never synced';
    return dayjs(connection.schemaSync.lastSyncedAt).fromNow();
  };

  // Handle connect
  const handleConnect = () => {
    if (onConnect) {
      onConnect(connection);
    }
  };

  // Handle edit
  const handleEdit = () => {
    if (onEdit) {
      onEdit(connection);
    }
  };

  // Handle test
  const handleTest = () => {
    if (onTest) {
      onTest(connection.id);
    }
  };

  // Handle sync
  const handleSync = () => {
    if (onSync) {
      onSync(connection.id);
    }
  };

  // Handle delete confirmation
  const handleDeleteConfirm = () => {
    setDeleteModalVisible(false);
    if (onDelete) {
      onDelete(connection.id);
    }
  };

  // Dropdown menu items
  const menuItems = [
    {
      key: 'test',
      icon: <PlayCircleOutlined />,
      label: 'Test Connection',
      onClick: handleTest,
    },
    {
      key: 'sync',
      icon: <SyncOutlined />,
      label: 'Sync Schema',
      onClick: handleSync,
      disabled: isSyncing,
    },
    { type: 'divider' },
    {
      key: 'edit',
      icon: <EditOutlined />,
      label: 'Edit',
      onClick: handleEdit,
    },
    {
      key: 'delete',
      icon: <DeleteOutlined />,
      label: 'Delete',
      danger: true,
      onClick: () => setDeleteModalVisible(true),
    },
  ];

  return (
    <>
      <Card
        hoverable
        style={{
          borderColor: isActive ? '#1890ff' : undefined,
          borderWidth: isActive ? '2px' : '1px',
        }}
        actions={[
          <Tooltip title="Connect" key="connect">
            <Button
              type="text"
              icon={<PlayCircleOutlined />}
              onClick={handleConnect}
            >
              Connect
            </Button>
          </Tooltip>,
          <Tooltip title="Test Connection" key="test">
            <Button
              type="text"
              icon={<PlayCircleOutlined spin={isTesting} />}
              onClick={handleTest}
              loading={isTesting}
            >
              Test
            </Button>
          </Tooltip>,
          <Tooltip title="Sync Schema" key="sync">
            <Button
              type="text"
              icon={<SyncOutlined spin={isSyncing} />}
              onClick={handleSync}
              loading={isSyncing}
            >
              Sync
            </Button>
          </Tooltip>,
          <Dropdown menu={{ items: menuItems }} trigger={['click']} key="more">
            <Button type="text" icon={<MoreOutlined />} />
          </Dropdown>,
        ]}
      >
        {/* Card Header */}
        <Card.Meta
          avatar={
            <div
              style={{
                width: 48,
                height: 48,
                borderRadius: 8,
                backgroundColor: provider.color,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <DatabaseOutlined style={{ fontSize: 24, color: '#fff' }} />
            </div>
          }
          title={
            <Space>
              <Title level={5} style={{ margin: 0 }}>
                {connection.name}
              </Title>
              {isActive && (
                <Tag color="blue" icon={<CheckCircleOutlined />}>
                  Active
                </Tag>
              )}
              {connection.isDefault && (
                <Tag color="green" icon={<SettingOutlined />}>
                  Default
                </Tag>
              )}
            </Space>
          }
          description={
            <Space direction="vertical" size={0}>
              <Tag color={provider.color}>{provider.label}</Tag>
            </Space>
          }
        />

        {/* Connection Details */}
        <div style={{ marginTop: 16 }}>
          <Space direction="vertical" size={4} style={{ width: '100%' }}>
            <Text type="secondary">
              <DatabaseOutlined /> {connection.host}:{connection.port}/
              {connection.database}
            </Text>

            <Space>
              <ClockCircleOutlined /> Last used: {formatLastUsed()}
            </Space>

            {/* Schema Sync Status */}
            {connection.schemaSync && (
              <Space>
                {connection.schemaSync.isSynced ? (
                  <Tag icon={<CheckCircleOutlined />} color="success">
                    Synced ({connection.schemaSync.tableCount} tables)
                  </Tag>
                ) : (
                  <Tag icon={<CloseCircleOutlined />} color="warning">
                    Not synced
                  </Tag>
                )}
                <Text type="secondary" style={{ fontSize: 12 }}>
                  {formatLastSynced()}
                </Text>
              </Space>
            )}
          </Space>
        </div>

        {/* Description */}
        {connection.description && (
          <div style={{ marginTop: 12 }}>
            <Text type="secondary">{connection.description}</Text>
          </div>
        )}
      </Card>

      {/* Delete Confirmation Modal */}
      <Modal
        title="Delete Connection"
        open={deleteModalVisible}
        onOk={handleDeleteConfirm}
        onCancel={() => setDeleteModalVisible(false)}
        okText="Delete"
        okButtonProps={{ danger: true }}
      >
        <p>
          Are you sure you want to delete the connection "
          <strong>{connection.name}</strong>"?
        </p>
        <p>This action cannot be undone.</p>
      </Modal>
    </>
  );
};

export default ConnectionCard;
