import React, { useState } from 'react';
import {
  Typography,
  Space,
  Tooltip,
  Button,
} from 'antd';
import {
  CopyOutlined,
  CheckOutlined,
  AlignLeftOutlined,
  CodeOutlined,
} from '@ant-design/icons';
import { escapeHtml } from '../../utils/security';

const { Text } = Typography;

const formatSql = (sql) => {
  let formatted = sql
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/\s*SELECT\s*/gi, '\nSELECT ')
    .replace(/\s*FROM\s*/gi, '\nFROM ')
    .replace(/\s*WHERE\s*/gi, '\nWHERE ')
    .replace(/\s*AND\s*/gi, '\n  AND ')
    .replace(/\s*OR\s*/gi, '\n  OR ')
    .replace(/\s*JOIN\s*/gi, '\nJOIN ')
    .replace(/\s*LEFT\s+JOIN\s*/gi, '\nLEFT JOIN ')
    .replace(/\s*RIGHT\s+JOIN\s*/gi, '\nRIGHT JOIN ')
    .replace(/\s*INNER\s+JOIN\s*/gi, '\nINNER JOIN ')
    .replace(/\s*OUTER\s+JOIN\s*/gi, '\nOUTER JOIN ')
    .replace(/\s*ON\s*/gi, '\n  ON ')
    .replace(/\s*GROUP\s+BY\s*/gi, '\nGROUP BY ')
    .replace(/\s*ORDER\s+BY\s*/gi, '\nORDER BY ')
    .replace(/\s*HAVING\s*/gi, '\nHAVING ')
    .replace(/\s*LIMIT\s*/gi, '\nLIMIT ')
    .replace(/\s*OFFSET\s*/gi, '\nOFFSET ')
    .replace(/\s*UNION\s*/gi, '\nUNION ')
    .replace(/\s*INSERT\s+INTO\s*/gi, '\nINSERT INTO ')
    .replace(/\s*VALUES\s*/gi, '\nVALUES ')
    .replace(/\s*UPDATE\s*/gi, '\nUPDATE ')
    .replace(/\s*SET\s*/gi, '\nSET ')
    .replace(/\s*DELETE\s+FROM\s*/gi, '\nDELETE FROM ')
    .replace(/\s*CREATE\s+TABLE\s*/gi, '\nCREATE TABLE ')
    .replace(/\s*ALTER\s+TABLE\s*/gi, '\nALTER TABLE ')
    .replace(/\s*DROP\s+TABLE\s*/gi, '\nDROP TABLE ')
    .replace(/\s*CASE\s*/gi, '\nCASE ')
    .replace(/\s*WHEN\s*/gi, '\n  WHEN ')
    .replace(/\s*THEN\s*/gi, '\n  THEN ')
    .replace(/\s*ELSE\s*/gi, '\n  ELSE ')
    .replace(/\s*END\s*/gi, '\nEND ')
    .replace(/\s*WITH\s*/gi, '\nWITH ')
    .trim();
  
  return formatted;
};

const SqlBlock = ({ sql, compact = false }) => {
  const [copied, setCopied] = useState(false);
  const [expanded, setExpanded] = useState(false);

  const shouldFormat = expanded && sql.length > 200;

  if (!sql) return null;

  const sqlToShow = shouldFormat ? formatSql(sql) : sql;

  const handleCopy = async () => {
    await navigator.clipboard.writeText(sql);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const highlightSql = (query) => {
    let safeQuery = escapeHtml(query);

    const keywords = [
      'SELECT', 'FROM', 'WHERE', 'AND', 'OR', 'NOT', 'IN', 'LIKE', 'BETWEEN',
      'JOIN', 'LEFT', 'RIGHT', 'INNER', 'OUTER', 'FULL', 'CROSS', 'ON',
      'GROUP', 'BY', 'HAVING', 'ORDER', 'ASC', 'DESC', 'LIMIT', 'OFFSET',
      'INSERT', 'INTO', 'VALUES', 'UPDATE', 'SET', 'DELETE', 'CREATE', 'TABLE',
      'ALTER', 'DROP', 'INDEX', 'PRIMARY', 'KEY', 'FOREIGN', 'REFERENCES',
      'NULL', 'IS', 'AS', 'DISTINCT', 'CASE', 'WHEN', 'THEN', 'ELSE', 'END',
      'UNION', 'ALL', 'EXISTS', 'WITH', 'RECURSIVE',
    ];

    const dataTypes = [
      'INT', 'INTEGER', 'BIGINT', 'SMALLINT', 'TINYINT',
      'VARCHAR', 'CHAR', 'TEXT', 'NVARCHAR', 'NCHAR',
      'DECIMAL', 'NUMERIC', 'FLOAT', 'REAL', 'MONEY',
      'DATE', 'TIME', 'DATETIME', 'TIMESTAMP',
      'BIT', 'BOOLEAN', 'BINARY', 'VARBINARY', 'BLOB',
      'UUID', 'JSON', 'XML',
    ];

    const functions = [
      'COUNT', 'SUM', 'AVG', 'MIN', 'MAX', 'COALESCE', 'NULLIF',
      'CONCAT', 'LENGTH', 'SUBSTRING', 'TRIM', 'UPPER', 'LOWER',
      'CAST', 'CONVERT', 'ROUND', 'FLOOR', 'CEIL',
      'ROW_NUMBER', 'RANK', 'DENSE_RANK', 'NTILE',
      'LAG', 'LEAD', 'FIRST_VALUE', 'LAST_VALUE',
      'OVER', 'PARTITION', 'ROWS', 'RANGE',
    ];

    let highlighted = safeQuery;

    highlighted = highlighted.replace(/&#x27;([^&#]*?)&#x27;/g, '<span class="sql-string">&#x27;$1&#x27;</span>');
    highlighted = highlighted.replace(/(--.*$)/gm, '<span class="sql-comment">$1</span>');
    highlighted = highlighted.replace(/(\/\*[\s\S]*?\*\/)/g, '<span class="sql-comment">$1</span>');
    highlighted = highlighted.replace(/\b(\d+\.?\d*)\b/g, '<span class="sql-number">$1</span>');

    keywords.forEach(keyword => {
      const regex = new RegExp(`\\b(${keyword})\\b`, 'gi');
      highlighted = highlighted.replace(regex, '<span class="sql-keyword">$1</span>');
    });

    dataTypes.forEach(type => {
      const regex = new RegExp(`\\b(${type})\\b`, 'gi');
      highlighted = highlighted.replace(regex, '<span class="sql-datatype">$1</span>');
    });

    functions.forEach(func => {
      const regex = new RegExp(`\\b(${func})\\s*\\(`, 'gi');
      highlighted = highlighted.replace(regex, '<span class="sql-function">$1</span>(');
    });

    return highlighted;
  };

  if (compact) {
    return (
      <code style={{
        backgroundColor: '#f5f5f5',
        padding: '6px 10px',
        borderRadius: 4,
        fontSize: 12,
        fontFamily: 'Monaco, Consolas, monospace',
        border: '1px solid #e8e8e8',
        display: 'block',
        overflowX: 'auto',
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word',
      }}>
        {sql}
      </code>
    );
  }

  return (
    <div style={{ marginTop: 8, borderRadius: 4, overflow: 'hidden' }}>
      <div style={{
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        padding: '6px 12px',
        backgroundColor: '#f0f5ff',
        borderBottom: '1px solid #d6e4ff',
      }}>
        <Space>
          <CodeOutlined style={{ color: '#1890ff' }} />
          <Text strong style={{ fontSize: 12, color: '#1890ff' }}>SQL Query</Text>
        </Space>

        <Space>
          {sql.length > 200 && (
            <Tooltip title={expanded ? 'Collapse' : 'Expand (Format)'}>
              <Button type="text" size="small" icon={<AlignLeftOutlined />} onClick={() => setExpanded(!expanded)} style={{ fontSize: 12, color: expanded ? '#1890ff' : '#8c8c8c' }} />
            </Tooltip>
          )}
          <Tooltip title={copied ? 'Copied!' : 'Copy SQL'}>
            <Button type="text" size="small" icon={copied ? <CheckOutlined /> : <CopyOutlined />} onClick={handleCopy} style={{ fontSize: 12, color: copied ? '#52c41a' : '#8c8c8c' }} />
          </Tooltip>
          <Tooltip title="Export .sql file">
            <Button
              type="text"
              size="small"
              icon={<CodeOutlined />}
              onClick={() => {
                const blob = new Blob([sql], { type: 'application/sql' });
                const url = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = url;
                a.download = `query_${Date.now()}.sql`;
                a.click();
                URL.revokeObjectURL(url);
              }}
              style={{ fontSize: 12, color: '#8c8c8c' }}
            />
          </Tooltip>
        </Space>
      </div>

      <div style={{
        padding: '12px',
        backgroundColor: '#fafafa',
        fontFamily: "'Monaco', 'Menlo', 'Ubuntu Mono', monospace",
        fontSize: 13,
        lineHeight: 1.6,
        overflowX: 'auto',
        overflowY: expanded ? 'auto' : 'hidden',
        maxHeight: expanded ? 'none' : '100px',
        position: 'relative',
        whiteSpace: shouldFormat ? 'pre' : 'pre-wrap',
        wordBreak: 'break-word',
      }}>
        <div dangerouslySetInnerHTML={{ __html: highlightSql(sqlToShow) }} />
        {!expanded && sql.length > 200 && (
          <div style={{
            position: 'absolute',
            bottom: 0,
            left: 0,
            right: 0,
            height: '40px',
            background: 'linear-gradient(transparent, #fafafa)',
            pointerEvents: 'none'
          }} />
        )}
      </div>

      <style>{`
        .sql-keyword { color: #0000ff; font-weight: 600; }
        .sql-datatype { color: #267f99; }
        .sql-string { color: #a31515; }
        .sql-number { color: #098658; }
        .sql-comment { color: #008000; font-style: italic; }
        .sql-function { color: #af00db; }
      `}</style>
    </div>
  );
};

export default SqlBlock;