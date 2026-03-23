import { useState, useEffect, useCallback } from 'react';
import { Table, Button, Space, Spin, Alert, Typography } from 'antd';
import { DownloadOutlined, ReloadOutlined } from '@ant-design/icons';
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
}) => {
    const [allRows, setAllRows] = useState(initialRows);
    const [isLoadingMore, setIsLoadingMore] = useState(false);

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

    // Prepare columns for Ant Design Table
    const tableColumns = columns.map((col) => ({
        title: col.columnName,
        dataIndex: col.columnName,
        key: col.columnName,
        width: 150,
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
    const handleExport = useCallback(() => {
        if (!allRows.length) return;

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

    return (
        <div style={{ width: '100%' }}>
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
                    <Text type="secondary">
                        {allRows.length} of {totalRows} rows
                        {resultId && totalPages > 1 && ` (Page ${currentPage}/${totalPages})`}
                    </Text>
                </Space>
                <Space>
                    <Button
                        icon={<DownloadOutlined />}
                        onClick={handleExport}
                        disabled={!allRows.length}
                        size="small"
                    >
                        Export CSV
                    </Button>
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
