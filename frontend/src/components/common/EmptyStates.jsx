import { Button, Space } from 'antd';
import { InboxOutlined, FileSearchOutlined, MessageOutlined, DatabaseOutlined, PlusOutlined } from '@ant-design/icons';

const EmptyState = ({
  icon,
  title,
  description,
  actionText,
  onAction,
  secondaryActionText,
  onSecondaryAction,
  className = '',
}) => {
  const IconComponent = icon || InboxOutlined;

  return (
    <div className={`empty-state-container ${className}`}>
      <div className="empty-icon">
        <IconComponent />
      </div>
      <div className="empty-title">{title}</div>
      {description && <div className="empty-description">{description}</div>}
      {(actionText || secondaryActionText) && (
        <div className="empty-actions">
          {actionText && (
            <Button type="primary" onClick={onAction}>
              {actionText}
            </Button>
          )}
          {secondaryActionText && (
            <Button onClick={onSecondaryAction}>
              {secondaryActionText}
            </Button>
          )}
        </div>
      )}
    </div>
  );
};

const EmptyConversations = ({ onCreateNew }) => (
  <EmptyState
    icon={<MessageOutlined />}
    title="No conversations yet"
    description="Start a new conversation to query your database"
    actionText="New Conversation"
    onAction={onCreateNew}
  />
);

const EmptyMessages = ({ onAskQuestion }) => (
  <EmptyState
    icon={<MessageOutlined />}
    title="No messages yet"
    description="Ask a question about your database to get started"
    actionText="Ask a Question"
    onAction={onAskQuestion}
  />
);

const EmptySearch = ({ onClearSearch }) => (
  <EmptyState
    icon={<FileSearchOutlined />}
    title="No results found"
    description="Try adjusting your search terms"
    actionText="Clear Search"
    onAction={onClearSearch}
  />
);

const EmptyTables = ({ onSyncSchema }) => (
  <EmptyState
    icon={<DatabaseOutlined />}
    title="No tables found"
    description="Sync your database schema to see available tables"
    actionText="Sync Schema"
    onAction={onSyncSchema}
  />
);

const EmptyResults = ({ onNewQuery }) => (
  <EmptyState
    icon={<InboxOutlined />}
    title="No results"
    description="Run a new query to see results here"
    actionText="New Query"
    onAction={onNewQuery}
  />
);

export { EmptyState, EmptyConversations, EmptyMessages, EmptySearch, EmptyTables, EmptyResults };
export default EmptyState;