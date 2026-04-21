import React, { useState, useMemo } from 'react';
import { Typography, Space, Tooltip, Button } from 'antd';
import {
  CopyOutlined,
  CheckOutlined,
  AlignLeftOutlined,
  CodeOutlined,
} from '@ant-design/icons';
import Editor from '@monaco-editor/react';

const { Text } = Typography;

/* ═══════════════════════════════════════════════════════════════════════════
 * SQL Formatter — indent-aware, string-literal safe
 *
 * Hierarchy:
 *   Level 0: top-level statements (WITH, SELECT …)
 *   +1 per open paren / CTE body / subquery
 *   Sub-clauses (AND, OR, ON) get +1 within their parent clause
 * ═══════════════════════════════════════════════════════════════════════════ */
const INDENT = '    '; // 4 spaces per level

const formatSql = (sql) => {
  if (!sql || !sql.trim()) return sql;

  // 1. Collapse whitespace
  let s = sql.replace(/\s+/g, ' ').trim();

  // 2. Protect string literals
  const literals = [];
  s = s.replace(/'[^']*'/g, (m) => {
    literals.push(m);
    return `__LIT${literals.length - 1}__`;
  });

  // 3. Inject markers before keywords (only when preceded by a space)
  //    Order matters — longer compound keywords first to prevent partial matches

  // Compound keywords (must come before single-word)
  const compounds = [
    'LEFT OUTER JOIN', 'RIGHT OUTER JOIN', 'FULL OUTER JOIN',
    'LEFT JOIN', 'RIGHT JOIN', 'INNER JOIN', 'CROSS JOIN', 'FULL JOIN',
    'GROUP BY', 'ORDER BY', 'PARTITION BY',
    'INSERT INTO', 'DELETE FROM',
    'CREATE TABLE', 'ALTER TABLE', 'DROP TABLE',
    'UNION ALL',
  ];
  for (const kw of compounds) {
    const p = new RegExp(`\\s+(${kw.replace(/\s+/g, '\\s+')})\\s+`, 'gi');
    s = s.replace(p, ` \n${kw.toUpperCase()} `);
  }

  // Single top-level keywords
  const topKeywords = [
    'WITH', 'SELECT', 'FROM', 'WHERE', 'HAVING', 'LIMIT', 'OFFSET',
    'UPDATE', 'SET', 'VALUES', 'UNION', 'INTERSECT', 'EXCEPT',
    'JOIN', 'CASE', 'END',
  ];
  for (const kw of topKeywords) {
    const p = new RegExp(`\\s+(${kw})\\b`, 'gi');
    s = s.replace(p, ` \n${kw} `);
  }

  // Sub-clause keywords — mark with a special prefix so we indent them extra
  const subKeywords = ['AND', 'OR', 'ON'];
  for (const kw of subKeywords) {
    const p = new RegExp(`\\s+(${kw})\\s+`, 'gi');
    s = s.replace(p, ` \n__SUB__${kw} `);
  }

  // WHEN / ELSE in CASE blocks
  s = s.replace(/\s+(WHEN)\s+/gi, ' \n__SUB__WHEN ');
  s = s.replace(/\s+(ELSE)\s+/gi, ' \n__SUB__ELSE ');

  // 4. Split into lines and compute indent based on parentheses nesting
  const rawLines = s.split('\n').map((l) => l.trim()).filter(Boolean);
  const result = [];
  let depth = 0;

  for (let line of rawLines) {
    const isSub = line.startsWith('__SUB__');
    if (isSub) {
      line = line.replace('__SUB__', '');
    }

    // Count open/close parens to track nesting depth changes
    // Adjust depth BEFORE rendering for closing-paren lines
    const opensInLine  = (line.match(/\(/g) || []).length;
    const closesInLine = (line.match(/\)/g) || []).length;

    // If line starts with ) or contains only ), decrease depth first
    const startsWithClose = /^\)/.test(line);
    if (startsWithClose && depth > 0) {
      depth -= 1;
    }

    const indentLevel = isSub ? depth + 1 : depth;
    const prefix = INDENT.repeat(Math.max(0, indentLevel));
    result.push(prefix + line);

    // Update depth based on net paren change
    const net = opensInLine - closesInLine - (startsWithClose ? -1 : 0);
    // only the net-opens inside the line matter (the leading close was already handled)
    if (!startsWithClose) {
      depth += net;
    } else {
      depth += (opensInLine - (closesInLine - 1));
    }
    if (depth < 0) depth = 0;
  }

  // 5. Restore string literals
  let out = result.join('\n');
  out = out.replace(/__LIT(\d+)__/g, (_, i) => literals[Number(i)]);

  return out;
};


/* ── Constants ─────────────────────────────────────────────────────────── */
const LINE_HEIGHT    = 19;
const EDITOR_PADDING = 16;
const MAX_HEIGHT     = 520;
const COLLAPSED_MAX  = 100;

/* ── Component ─────────────────────────────────────────────────────────── */
const SqlBlock = ({ sql, compact = false }) => {
  const [copied, setCopied]       = useState(false);
  const [expanded, setExpanded]   = useState(false);

  if (!sql) return null;

  const shouldFormat = expanded && sql.length > 200;
  const sqlToShow = useMemo(
    () => (shouldFormat ? formatSql(sql) : sql),
    [sql, shouldFormat],
  );

  const lineCount    = useMemo(() => sqlToShow.split('\n').length, [sqlToShow]);
  const naturalHeight = lineCount * LINE_HEIGHT + EDITOR_PADDING;

  const editorHeight = (() => {
    if (!expanded && sql.length > 200) {
      return Math.min(naturalHeight, COLLAPSED_MAX);
    }
    return Math.min(naturalHeight, MAX_HEIGHT);
  })();

  const handleCopy = async () => {
    await navigator.clipboard.writeText(sql);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  /** Monaco options — read-only, original monospace font */
  const baseOptions = {
    readOnly:               true,
    domReadOnly:            true,
    minimap:                { enabled: false },
    scrollBeyondLastLine:   false,
    automaticLayout:        true,
    wordWrap:               expanded ? 'off' : 'on',
    lineNumbers:            expanded ? 'on' : 'off',
    folding:                expanded,
    glyphMargin:            false,
    lineDecorationsWidth:   expanded ? 4 : 0,
    lineNumbersMinChars:    expanded ? 3 : 0,
    renderLineHighlight:    'none',
    overviewRulerLanes:     0,
    hideCursorInOverviewRuler: true,
    overviewRulerBorder:    false,
    scrollbar: {
      vertical:             expanded ? 'auto' : 'hidden',
      horizontal:           'auto',
      useShadows:           false,
      verticalScrollbarSize: 6,
      horizontalScrollbarSize: 6,
    },
    contextmenu:            false,
    tabSize:                4,
    fontFamily:             "'Monaco', 'Menlo', 'Ubuntu Mono', monospace",
    cursorBlinking:         'solid',
    selectionHighlight:     false,
    occurrencesHighlight:   'off',
    matchBrackets:          'near',
  };

  /* ── Compact mode ──────────────────────────────────────────────────── */
  if (compact) {
    return (
      <div style={{ borderRadius: 4, overflow: 'hidden', border: '1px solid #e8e8e8' }}>
        <Editor
          height={Math.min(lineCount * LINE_HEIGHT + EDITOR_PADDING, 200)}
          defaultLanguage="sql"
          value={sql}
          theme="vs-light"
          options={{ ...baseOptions, fontSize: 12, lineNumbers: 'off', wordWrap: 'on' }}
        />
      </div>
    );
  }

  /* ── Full mode ─────────────────────────────────────────────────────── */
  return (
    <div style={{
      marginTop: 8,
      borderRadius: 6,
      overflow: 'hidden',
      border: '1px solid #dbe4ff',
      boxShadow: '0 1px 3px rgba(24,144,255,0.06)',
    }}>

      {/* ── Toolbar ── */}
      <div style={{
        display:         'flex',
        justifyContent:  'space-between',
        alignItems:      'center',
        padding:         '6px 12px',
        backgroundColor: '#f0f5ff',
        borderBottom:    '1px solid #d6e4ff',
      }}>
        <Space>
          <CodeOutlined style={{ color: '#1890ff' }} />
          <Text strong style={{ fontSize: 12, color: '#1890ff' }}>SQL Query</Text>
        </Space>

        <Space>
          {sql.length > 200 && (
            <Tooltip title={expanded ? 'Collapse' : 'Expand & Format'}>
              <Button
                type="text" size="small"
                icon={<AlignLeftOutlined />}
                onClick={() => setExpanded(!expanded)}
                style={{ fontSize: 12, color: expanded ? '#1890ff' : '#8c8c8c' }}
              />
            </Tooltip>
          )}
          <Tooltip title={copied ? 'Copied!' : 'Copy SQL'}>
            <Button
              type="text" size="small"
              icon={copied ? <CheckOutlined /> : <CopyOutlined />}
              onClick={handleCopy}
              style={{ fontSize: 12, color: copied ? '#52c41a' : '#8c8c8c' }}
            />
          </Tooltip>
          <Tooltip title="Export .sql file">
            <Button
              type="text" size="small"
              icon={<CodeOutlined />}
              onClick={() => {
                const blob = new Blob([sql], { type: 'application/sql' });
                const url  = URL.createObjectURL(blob);
                const a    = document.createElement('a');
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

      {/* ── Monaco Editor ── */}
      <div style={{ position: 'relative' }}>
        <Editor
          height={editorHeight}
          defaultLanguage="sql"
          value={sqlToShow}
          theme="vs-light"
          options={{ ...baseOptions, fontSize: 13 }}
        />

        {/* Fade overlay when collapsed */}
        {!expanded && sql.length > 200 && (
          <div style={{
            position: 'absolute',
            bottom: 0, left: 0, right: 0,
            height: 40,
            background: 'linear-gradient(transparent, #ffffff)',
            pointerEvents: 'none',
          }} />
        )}
      </div>
    </div>
  );
};

export default SqlBlock;