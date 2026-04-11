import React, { useState, useRef, useEffect } from 'react';
import {
  Input,
  Button,
  Typography,
  Tooltip,
} from 'antd';
import {
  SendOutlined,
  LoadingOutlined,
} from '@ant-design/icons';

const { TextArea } = Input;
const { Text } = Typography;

const ChatInput = ({
  onSend,
  isLoading = false,
  disabled = false,
  placeholder = 'Ask a question about your database...',
  maxLength = 5000,
  initialValue = '',
}) => {
  const [value, setValue] = useState(initialValue);
  const textAreaRef = useRef(null);

  useEffect(() => {
    if (initialValue) {
      setValue(initialValue);
      setTimeout(() => textAreaRef.current?.focus?.(), 100);
    }
  }, [initialValue]);

  useEffect(() => {
    textAreaRef.current?.resizeTextArea?.();
  }, [value]);

  const handleChange = (e) => {
    const newValue = e.target.value;
    if (newValue.length <= maxLength) {
      setValue(newValue);
    }
  };

  const handleSend = async () => {
    const trimmedValue = value.trim();
    if (!trimmedValue || isLoading || disabled) return;

    setValue('');
    if (onSend) {
      await onSend(trimmedValue);
    }
    setTimeout(() => textAreaRef.current?.focus?.(), 100);
  };

  const handleKeyDown = (e) => {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      handleSend();
    }
  };

  const charCount = value.length;
  const isNearLimit = charCount > maxLength * 0.9;
  const canSend = value.trim() && !disabled;

  return (
    <div
      style={{
        borderTop: '1px solid #f0f0f0',
        padding: '12px 16px',
        backgroundColor: '#fff',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 8,
          backgroundColor: '#f5f5f5',
          borderRadius: 8,
          padding: '8px 12px',
          border: '1px solid #e8e8e8',
        }}
      >
        <TextArea
          ref={textAreaRef}
          value={value}
          onChange={handleChange}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
          autoSize={{ minRows: 1, maxRows: 4 }}
          disabled={disabled || isLoading}
          bordered={false}
          style={{
            flex: 1,
            backgroundColor: 'transparent',
            fontSize: 14,
          }}
          status={isNearLimit ? 'warning' : undefined}
        />
        <Tooltip title={isLoading ? 'Sending...' : (canSend ? 'Send (Ctrl+Enter)' : 'Enter to send')}>
          <Button
            type="primary"
            icon={isLoading ? <LoadingOutlined spin /> : <SendOutlined />}
            onClick={handleSend}
            loading={isLoading}
            disabled={!canSend}
            size="small"
            style={{
              borderRadius: 6,
              width: 32,
              height: 32,
              backgroundColor: isLoading ? '#1890ff' : undefined,
            }}
          />
        </Tooltip>
      </div>

      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          marginTop: 6,
        }}
      >
        <Text type="secondary" style={{ fontSize: 11 }}>
          Ctrl+Enter to send • Shift+Enter for new line
        </Text>
        <Text type={isNearLimit ? 'warning' : 'secondary'} style={{ fontSize: 11 }}>
          {charCount.toLocaleString()} / {maxLength.toLocaleString()}
        </Text>
      </div>
    </div>
  );
};

export default ChatInput;