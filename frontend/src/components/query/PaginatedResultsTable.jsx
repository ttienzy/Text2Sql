import { useState, useEffect, useCallback, useRef } from 'react';
import { Table, Button, Space, Spin, Alert, Typography, Dropdown, Tooltip } from 'antd';
import { DownloadOutlined, ReloadOutlined, FileExcelOutlined, MoreOutlined } from '@ant-design/icons';
import { useQueryPagination } from '../../hooks/useQueryPagination';
import { FixedSizeList } from 'react-window';

const { Text } = Typography;

/**
 * Paginated results table with lazy loading and virtual scrolling
 * @param {Object} props
 * @param {Array} props.initialRows - First page of rows
 * @param {Array} props.columns - Column metadata
 * @param {number} props.totalRows - Total number of rows
 * @param {string} props.resultId - Cached result ID for pagination
 * @param {boolean} props.hasMore - Whether more pages available
 */
const PaginatedResultsTable = ({
    initialRows = [],
    columns = [],
    totalRows = 0,
    resultId = null,
    hasMore = false,
    executionTimeMs,
}) => {
    const [allRows, setAllRows] = useState(initialRows);
    const [isLoadingMore, setIsLoadingMore] = useState(false);
    const [columnWidths, setColumnWidths] = useState({});
    const tableRef = useRef(null);

    const {
        loading,
        error,
        currentPage,
        hasMore: paginationHasMore,
        totalPages,
        loadNext,
        getAllLoadedRows,
    } = useQueryPagination(resultId);

    // Update allRows when new pages loaded
    useEffect(() => {
        if (resultId) {
            const loadedRows = getAllLoadedRows();
            if (loadedRows.length > 0) {
                setAllRows([...initialRows, ...loadedRows]);
            }
        }
    }, [resultId, getAllLoadedRows, initialRows]);

    // Handle column resize
    const handleColumnResize = useCallback((columnKey, width) => {
        setColumnWidths(prev => ({
            ...prev,
            [columnKey]: width,
        }));
    }, []);

    // Prepare columns for Ant Design Table with resizing
    const tableColumns = columns.map((col) => ({
        title: (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <span>{col.columnName}</span>
            </div>
        ),
        dataIndex: col.columnName,
        key: col.columnName,
        width: columnWidths[col.columnName] || 150,
        minWidth: 80,
        ellipsis: true,
        render: (value) => {
            if (value === null || value === undefined) {
                return <Text type="secondary">NULL</Text>;
            }
            if (typeof value === 'boolean') {
                return value ? 'true' : 'false';
            }
            if (typeof value === 'object') {
                return JSON.stringify(value);
            }
            return String(value);
        },
    }));

    // Load more handler
    const handleLoadMore = useCallback(async () => {
        if (!resultId || !hasMore || loading) return;

        setIsLoadingMore(true);
        try {
            await loadNext();
        } finally {
            setIsLoadingMore(false);
        }
    }, [resultId, hasMore, loading, loadNext]);

    // Infinite scroll handler
    const handleScroll = useCallback((e) => {
        const { scrollTop, scrollHeight, clientHeight } = e.target;
        const isNearBottom = scrollHeight - scrollTop - clientHeight < 100;

        if (isNearBottom && !loading && hasMore && resultId) {
            handleLoadMore();
        }
    }, [loading, hasMore, resultId, handleLoadMore]);

    // Export to CSV
    const handleExport = useCallback((format = 'csv') => {
        if (!allRows.length) return;

        if (format === 'excel') {
            // Convert to Excel-compatible XML format
            const xmlContent = `<?xml version="1.0" encoding="UTF-8"?>
<?mso-application progid="Excel.Sheet"?>
<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet"
  xmlns:o="urn:schemas-microsoft-com:office:office"
  xmlns:x="urn:schemas-microsoft-com:office:excel"
  xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
  <Worksheet ss:Name="Query Results">
    <Table>
      <Row>
        ${columns.map(col => `<Cell><Data ss:Type="String">${col.columnName}</Data></Cell>`).join('')}
      </Row>
      ${allRows.map(row => `
      <Row>
        ${columns.map(col => {
          const value = row[col.columnName];
          return `<Cell><Data ss:Type="${typeof value === 'number' ? 'Number' : 'String'}">${value ?? ''}</Data></Cell>`;
        }).join('')}
      </Row>`).join('')}
    </Table>
  </Worksheet>
</Workbook>`;

            const blob = new Blob([xmlContent], { type: 'application/vnd.ms-excel' });
            const link = document.createElement('a');
            link.href = URL.createObjectURL(blob);
            link.download = `query_results_${Date.now()}.xls`;
            link.click();
            return;
        }

        // Default CSV export
        const csvContent = [
            // Header
            columns.map(col => col.columnName).join(','),
            // Rows
            ...allRows.map(row =>
                columns.map(col => {
                    const value = row[col.columnName];
                    if (value === null || value === undefined) return '';
                    if (typeof value === 'string' && value.includes(',')) {
                        return `"${value}"`;
                    }
                    return value;
                }).join(',')
            )
        ].join('\n');

        const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
        const link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = `query_results_${Date.now()}.csv`;
        link.click();
    }, [allRows, columns]);

    // Export menu items
    const exportMenuItems = {
        items: [
            {
                key: 'csv',
                icon: <DownloadOutlined />,
                label: 'Export CSV',
                onClick: () => handleExport('csv'),
            },
            {
                key: 'excel',
                icon: <FileExcelOutlined />,
                label: 'Export Excel',
                onClick: () => handleExport('excel'),
            },
        ],
    };

    if (error) {
        return (
            <Alert
                type="error"
                message="Failed to load results"
                description={error}
                showIcon
            />
        );
    }

    const getQueryTimeClass = (ms) => {
        if (ms < 500) return 'fast';
        if (ms < 2000) return 'medium';
        return 'slow';
    };

    const formatTime = (ms) => {
        if (ms < 1000) return `${ms}ms`;
        return `${(ms / 1000).toFixed(1)}s`;
    };

    return (
        <div style={{ width: '100%' }} className="fade-in">
            {/* Header with stats and actions */}
            <div style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                marginBottom: 12,
                padding: '8px 12px',
                background: '#fafafa',
                borderRadius: 4,
            }}>
                <Space>
                    <Text strong>Query Results</Text>
                    <span className={`query-time-badge ${executionTimeMs ? getQueryTimeClass(executionTimeMs) : ''}`}>
                        {executionTimeMs ? formatTime(executionTimeMs) : ''}
                    </span>
                    <Text type="secondary">
                        {allRows.length} of {totalRows} rows
                        {resultId && totalPages > 1 && ` (Page ${currentPage}/${totalPages})`}
                    </Text>
                </Space>
                <Space>
                    {allRows.length > 0 && (
                        <Dropdown menu={exportMenuItems} trigger={['click']}>
                            <Button size="small" icon={<MoreOutlined />}>
                                Export
                            </Button>
                        </Dropdown>
                    )}
                    {hasMore && resultId && (
                        <Button
                            icon={<ReloadOutlined />}
                            onClick={handleLoadMore}
                            loading={isLoadingMore}
                            size="small"
                        >
                            Load More
                        </Button>
                    )}
                </Space>
            </div>

            {/* Table with virtual scrolling */}
            <div
                className="table-sticky-header"
                ref={tableRef}
                style={{
                    maxHeight: 600,
                    overflow: 'auto',
                    border: '1px solid #f0f0f0',
                    borderRadius: 4,
                }}
                onScroll={handleScroll}
            >
                <Table
                    dataSource={allRows}
                    columns={tableColumns}
                    rowKey={(record, index) => index}
                    pagination={false}
                    scroll={{ x: 'max-content', y: 550 }}
                    size="small"
                    loading={loading && allRows.length === 0}
                    resizable
                />
            </div>

            {/* Loading indicator for pagination */}
            {isLoadingMore && (
                <div style={{ textAlign: 'center', padding: 16 }}>
                    <Spin tip="Loading more results..." />
                </div>
            )}

            {/* End of results indicator */}
            {!hasMore && allRows.length > 0 && (
                <div style={{ textAlign: 'center', padding: 16 }}>
                    <Text type="secondary">
                        All {totalRows} rows loaded
                    </Text>
                </div>
            )}
        </div>
    );
};

export default PaginatedResultsTable;
