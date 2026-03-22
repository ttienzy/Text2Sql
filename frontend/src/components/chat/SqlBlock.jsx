import React, { useState } from 'react';
import {
  Typography,
  Space,
  Tooltip,
  Collapse,
  Button,
} from 'antd';
import {
  CopyOutlined,
  CheckOutlined,
  ExpandOutlined,
  CompressOutlined,
  CodeOutlined,
} from '@ant-design/icons';
import { escapeHtml } from '../../utils/security';

const { Text } = Typography;
const { Panel } = Collapse;

/**
 * SqlBlock - Component for displaying SQL queries with syntax highlighting
 * @param {Object} props
 * @param {string} props.sql - The SQL query string
 * @param {boolean} props.error - Whether the SQL resulted in an error
 * @param {boolean} props.expandable - Whether the block can be expanded (default: true)
 * @param {boolean} props.compact - Compact mode for inline display (default: false)
 */
const SqlBlock = ({ sql, error = false, expandable = true, compact = false }) => {
  const [copied, setCopied] = useState(false);
  const [expanded, setExpanded] = useState(false);

  if (!sql) return null;

  const isLongQuery = sql.length > 200;
  const displaySql = expanded || !isLongQuery ? sql : sql.substring(0, 200) + '...';

  const handleCopy = async () => {
    await navigator.clipboard.writeText(sql);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  // Compact mode - simple inline display
  if (compact) {
    return (
      <div
        style={{
          backgroundColor: '#f5f5f5',
          padding: '6px 10px',
          borderRadius: 4,
          fontSize: 12,
          fontFamily: 'Monaco, Consolas, monospace',
          border: '1px solid #e8e8e8',
          overflowX: 'auto',
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
        }}
      >
        <code>{sql}</code>
      </div>
    );
  }

  // Simple SQL syntax highlighting (with XSS prevention)
  const highlightSql = (query) => {
    // First escape all HTML to prevent XSS
    let safeQuery = escapeHtml(query);

    // SQL keywords to highlight
    const keywords = [
      'SELECT', 'FROM', 'WHERE', 'AND', 'OR', 'NOT', 'IN', 'LIKE', 'BETWEEN',
      'JOIN', 'LEFT', 'RIGHT', 'INNER', 'OUTER', 'FULL', 'CROSS', 'ON',
      'GROUP', 'BY', 'HAVING', 'ORDER', 'ASC', 'DESC', 'LIMIT', 'OFFSET',
      'INSERT', 'INTO', 'VALUES', 'UPDATE', 'SET', 'DELETE', 'CREATE', 'TABLE',
      'ALTER', 'DROP', 'INDEX', 'PRIMARY', 'KEY', 'FOREIGN', 'REFERENCES',
      'NULL', 'IS', 'AS', 'DISTINCT', 'COUNT', 'SUM', 'AVG', 'MIN', 'MAX',
      'CASE', 'WHEN', 'THEN', 'ELSE', 'END', 'UNION', 'ALL', 'EXISTS',
      'WITH', 'RECURSIVE', 'OVER', 'PARTITION', 'WINDOW', 'ROW_NUMBER',
      'RANK', 'DENSE_RANK', 'LAG', 'LEAD', 'FIRST_VALUE', 'LAST_VALUE',
    ];

    // Data types to highlight
    const dataTypes = [
      'INT', 'INTEGER', 'BIGINT', 'SMALLINT', 'TINYINT',
      'VARCHAR', 'CHAR', 'TEXT', 'NVARCHAR', 'NCHAR',
      'DECIMAL', 'NUMERIC', 'FLOAT', 'REAL', 'MONEY',
      'DATE', 'TIME', 'DATETIME', 'TIMESTAMP', 'SMALLDATETIME',
      'BIT', 'BOOLEAN', 'BINARY', 'VARBINARY', 'BLOB',
      'UUID', 'JSON', 'XML', 'ARRAY',
    ];

    let highlighted = safeQuery;

    // Escape any HTML in the original query before highlighting
    // Keywords are already escaped, so we match and wrap in spans
    // Note: We need to be careful not to double-escape the span tags

    // Highlight keywords (case-insensitive) - use regex that matches escaped or unescaped
    keywords.forEach(keyword => {
      const regex = new RegExp(`\\b(${keyword})\\b`, 'gi');
      // Only replace if not already inside a span tag
      highlighted = highlighted.replace(regex, '<span class="sql-keyword">$1</span>');
    });

    // Highlight data types
    dataTypes.forEach(type => {
      const regex = new RegExp(`\\b(${type})\\b`, 'gi');
      highlighted = highlighted.replace(regex, '<span class="sql-datatype">$1</span>');
    });

    // Highlight strings (quoted values) - these are already escaped
    // Match &#x27; (escaped single quote) or actual quotes after escaping
    highlighted = highlighted.replace(/&#x27;([^&#]*?)&#x27;/g, '<span class="sql-string">&#x27;$1&#x27;</span>');

    // Highlight numbers
    highlighted = highlighted.replace(/\b(\d+)\b/g, '<span class="sql-number">$1</span>');

    // Highlight comments
    highlighted = highlighted.replace(/(--.*$)/gm, '<span class="sql-comment">$1</span>');

    return highlighted;
  };

  return (
    <div
      style={{
        marginTop: 8,
        borderRadius: 4,
        overflow: 'hidden',
      }}
    >
      {/* Header */}
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          padding: '6px 12px',
          backgroundColor: error ? '#fff2f0' : '#f0f5ff',
          borderBottom: `1px solid ${error ? '#ffccc7' : '#d6e4ff'}`,
        }}
      >
        <Space>
          <CodeOutlined style={{ color: error ? '#ff4d4f' : '#1890ff' }} />
          <Text strong style={{ fontSize: 12, color: error ? '#ff4d4f' : '#1890ff' }}>
            SQL Query
          </Text>
        </Space>

        <Space>
          {isLongQuery && expandable && (
            <Tooltip title={expanded ? 'Collapse' : 'Expand'}>
              <Button
                type="text"
                size="small"
                icon={expanded ? <CompressOutlined /> : <ExpandOutlined />}
                onClick={() => setExpanded(!expanded)}
                style={{ fontSize: 12 }}
              />
            </Tooltip>
          )}
          <Tooltip title={copied ? 'Copied!' : 'Copy SQL'}>
            <Button
              type="text"
              size="small"
              icon={copied ? <CheckOutlined /> : <CopyOutlined />}
              onClick={handleCopy}
              style={{
                fontSize: 12,
                color: copied ? '#52c41a' : '#8c8c8c',
              }}
            />
          </Tooltip>
        </Space>
      </div>

      {/* SQL Content */}
      <div
        style={{
          padding: '12px',
          backgroundColor: '#fafafa',
          fontFamily: "'Monaco', 'Menlo', 'Ubuntu Mono', monospace",
          fontSize: 13,
          lineHeight: 1.5,
          overflowX: 'auto',
          whiteSpace: expanded ? 'pre-wrap' : 'pre',
          wordBreak: 'break-word',
        }}
        dangerouslySetInnerHTML={{ __html: highlightSql(displaySql) }}
      />

      <style>{`
        .sql-keyword {
          color: #0000ff;
          font-weight: 600;
        }
        .sql-datatype {
          color: #267f99;
        }
        .sql-string {
          color: #a31515;
        }
        .sql-number {
          color: #098658;
        }
        .sql-comment {
          color: #008000;
          font-style: italic;
        }
      `}</style>
    </div>
  );
};

export default SqlBlock;
