import React, { useState, useRef, useEffect } from 'react';
import {
  Input,
  Button,
  Typography,
  Tooltip,
  Space,
  Skeleton,
} from 'antd';
import {
  SendOutlined,
  LoadingOutlined,
} from '@ant-design/icons';

const { TextArea } = Input;
const { Text } = Typography;

/**
 * ChatInput - Component for user input in the chat
 * @param {Object} props
 * @param {Function} props.onSend - Callback when message is sent
 * @param {boolean} props.isLoading - Whether a request is in progress
 * @param {boolean} props.disabled - Whether input is disabled
 * @param {string} props.placeholder - Placeholder text
 * @param {number} props.maxLength - Maximum character length
 * @param {string} props.initialValue - Initial value for the input (from context)
 */
const ChatInput = ({
  onSend,
  isLoading = false,
  disabled = false,
  placeholder = 'Ask a question about your database...',
  maxLength = 5000,
  initialValue = '',
}) => {
  const [value, setValue] = useState(initialValue);
  const [isFocused, setIsFocused] = useState(false);
  const textAreaRef = useRef(null);

  // Update value when initialValue changes (from context)
  useEffect(() => {
    if (initialValue) {
      setValue(initialValue);
      // Focus the textarea after setting value
      setTimeout(() => {
        textAreaRef.current?.focus?.();
      }, 100);
    }
  }, [initialValue]);

  // Auto-resize textarea
  useEffect(() => {
    if (textAreaRef.current) {
      textAreaRef.current.resizeTextArea?.();
    }
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

    // Clear input and trigger send
    setValue('');
    if (onSend) {
      await onSend(trimmedValue);
    }

    // Refocus after send
    setTimeout(() => {
      textAreaRef.current?.focus?.();
    }, 100);
  };

  const handleKeyDown = (e) => {
    // Ctrl+Enter to send
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      handleSend();
      return;
    }

    // Shift+Enter for new line (default behavior)
    if (e.key === 'Enter' && e.shiftKey) {
      return; // Allow default behavior
    }

    // Regular Enter - don't send (use Ctrl+Enter instead)
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      // Optionally show hint that Ctrl+Enter is needed
    }
  };

  // Character count display
  const charCount = value.length;
  const isNearLimit = charCount > maxLength * 0.9;

  return (
    <div style={{
      borderTop: '1px solid #f0f0f0',
      padding: '12px 16px',
      backgroundColor: '#fff',
    }}>
      <div style={{ display: 'flex', gap: 8, alignItems: 'flex-end' }}>
        <TextArea
          ref={textAreaRef}
          value={value}
          onChange={handleChange}
          onKeyDown={handleKeyDown}
          onFocus={() => setIsFocused(true)}
          onBlur={() => setIsFocused(false)}
          placeholder={placeholder}
          autoSize={{ minRows: 2, maxRows: 8 }}
          disabled={disabled || isLoading}
          style={{
            flex: 1,
            borderColor: isFocused ? '#1890ff' : undefined,
          }}
          status={isNearLimit ? 'warning' : undefined}
        />
        <Button
          type="primary"
          icon={isLoading ? <LoadingOutlined /> : <SendOutlined />}
          onClick={handleSend}
          loading={isLoading}
          disabled={!value.trim() || disabled}
          style={{
            height: 'auto',
            minHeight: 66,
            alignSelf: 'flex-end',
            padding: '12px 20px',
          }}
        >
          {isLoading ? 'Sending...' : 'Send'}
        </Button>
      </div>

      {/* Helper text */}
      <div style={{
        display: 'flex',
        justifyContent: 'space-between',
        marginTop: 8,
      }}>
        <Text type="secondary" style={{ fontSize: 12 }}>
          <Tooltip title="Press Ctrl+Enter to send">
            <span style={{ cursor: 'help' }}>
              Ctrl+Enter to send • Shift+Enter for new line
            </span>
          </Tooltip>
        </Text>
        <Text
          type={isNearLimit ? 'warning' : 'secondary'}
          style={{ fontSize: 12 }}
        >
          {charCount.toLocaleString()} / {maxLength.toLocaleString()}
        </Text>
      </div>
    </div>
  );
};

export default ChatInput;
