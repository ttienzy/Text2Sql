import React, { useState } from 'react';
import { 
  Table, 
  Typography, 
  Space, 
  Button, 
  Tooltip,
  Tag,
} from 'antd';
import { 
  DownloadOutlined, 
  InfoCircleOutlined,
  TableOutlined,
} from '@ant-design/icons';

const { Text } = Typography;

/**
 * ResultTable - Component for displaying query results in a table format
 * @param {Object} props
 * @param {Array} props.data - Array of result objects
 * @param {number} props.rowCount - Total number of rows
 * @param {number} props.pageSize - Number of rows per page (default: 10)
 */
const ResultTable = ({ data, rowCount, pageSize = 10 }) => {
  const [currentPage, setCurrentPage] = useState(1);
  const [pageSizeState, setPageSizeState] = useState(pageSize);
  
  if (!data || !Array.isArray(data) || data.length === 0) {
    return null;
  }
  
  // Get column names from the first row
  const columns = Object.keys(data[0] || {}).map(key => ({
    title: (
      <Space>
        <Text strong style={{ fontSize: 12 }}>{key}</Text>
        <Tag color="default" style={{ marginLeft: 4, fontSize: 10 }}>
          {typeof data[0][key] === 'number' ? 'number' : 
           typeof data[0][key] === 'boolean' ? 'boolean' : 
           typeof data[0][key] === 'object' ? 'json' : 'text'}
        </Tag>
      </Space>
    ),
    dataIndex: key,
    key: key,
    width: 150,
    ellipsis: true,
    sorter: (a, b) => {
      const aVal = a[key];
      const bVal = b[key];
      if (typeof aVal === 'number' && typeof bVal === 'number') {
        return aVal - bVal;
      }
      return String(aVal || '').localeCompare(String(bVal || ''));
    },
    render: (value) => {
      if (value === null || value === undefined) {
        return <Text type="secondary" style={{ fontStyle: 'italic' }}>NULL</Text>;
      }
      if (typeof value === 'boolean') {
        return <Tag color={value ? 'green' : 'red'}>{value.toString()}</Tag>;
      }
      if (typeof value === 'object') {
        return <Text code style={{ fontSize: 11 }}>{JSON.stringify(value)}</Text>;
      }
      return <Text>{String(value)}</Text>;
    },
  }));
  
  // Export to CSV
  const handleExportCsv = () => {
    if (data.length === 0) return;
    
    const headers = columns.map(col => col.title.props?.children?.[0]?.props?.children || col.dataIndex);
    const csvContent = [
      headers.join(','),
      ...data.map(row => 
        columns.map(col => {
          const value = row[col.dataIndex];
          if (value === null || value === undefined) return '';
          if (typeof value === 'object') return JSON.stringify(value);
          if (typeof value === 'string' && value.includes(',')) return `"${value}"`;
          return String(value);
        }).join(',')
      )
    ].join('\n');
    
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `query_results_${new Date().toISOString().slice(0, 10)}.csv`;
    link.click();
    URL.revokeObjectURL(link.href);
  };
  
  // Row count display
  const displayRowCount = rowCount || data.length;
  
  return (
    <div style={{ marginTop: 12 }}>
      {/* Header */}
      <div 
        style={{ 
          display: 'flex', 
          justifyContent: 'space-between', 
          alignItems: 'center',
          padding: '8px 12px',
          backgroundColor: '#f6ffed',
          borderRadius: '4px 4px 0 0',
          border: '1px solid #b7eb8f',
        }}
      >
        <Space>
          <TableOutlined style={{ color: '#52c41a' }} />
          <Text strong style={{ fontSize: 12, color: '#52c41a' }}>
            Query Results
          </Text>
          <Tooltip title="Number of rows returned">
            <Tag color="green">{displayRowCount} {displayRowCount === 1 ? 'row' : 'rows'}</Tag>
          </Tooltip>
        </Space>
        
        <Button
          type="text"
          size="small"
          icon={<DownloadOutlined />}
          onClick={handleExportCsv}
          style={{ fontSize: 12 }}
        >
          Export CSV
        </Button>
      </div>
      
      {/* Table */}
      <Table
        dataSource={data}
        columns={columns}
        rowKey={(record, index) => record.id || index}
        size="small"
        pagination={{
          current: currentPage,
          pageSize: pageSizeState,
          total: displayRowCount,
          showSizeChanger: true,
          showQuickJumper: true,
          pageSizeOptions: ['5', '10', '20', '50', '100'],
          showTotal: (total, range) => (
            <Text type="secondary" style={{ fontSize: 12 }}>
              {range[0]}-{range[1]} of {total} rows
            </Text>
          ),
          onChange: (page, size) => {
            setCurrentPage(page);
            setPageSizeState(size);
          },
        }}
        scroll={{ x: 'max-content' }}
        locale={{
          emptyText: 'No results found',
        }}
        style={{
          border: '1px solid #f0f0f0',
          borderTop: 'none',
          borderRadius: '0 0 4px 4px',
        }}
      />
      
      {/* Info footer */}
      <div style={{ marginTop: 8 }}>
        <Space>
          <InfoCircleOutlined style={{ color: '#8c8c8c', fontSize: 12 }} />
          <Text type="secondary" style={{ fontSize: 11 }}>
            Click column headers to sort. Use pagination to navigate through results.
          </Text>
        </Space>
      </div>
    </div>
  );
};

export default ResultTable;
