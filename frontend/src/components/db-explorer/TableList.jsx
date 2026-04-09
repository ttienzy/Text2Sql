import { useState } from 'react';
import { Input, List, Tag, Space, Empty, Spin, Button, Segmented, Select, Alert } from 'antd';
import { SearchOutlined, TableOutlined, StarFilled, SortAscendingOutlined, SortDescendingOutlined } from '@ant-design/icons';
import { formatNumber } from '../../utils/formatters';

const { Search } = Input;

const ROLE_COLORS = {
    Master: '#1890ff',
    Transaction: '#52c41a',
    Bridge: '#faad14',
    Configuration: '#722ed1',
    LogAudit: '#8c8c8c',
};

const ROLE_LABELS = {
    Master: 'Master',
    Transaction: 'Transaction',
    Bridge: 'Bridge',
    Configuration: 'Config',
    LogAudit: 'Log/Audit',
};

const TableList = ({ tables, loading, selectedTable, onTableSelect, pinnedTables = [], moduleFilter }) => {
    const [searchText, setSearchText] = useState('');
    const [roleFilter, setRoleFilter] = useState('All');
    const [sortBy, setSortBy] = useState('name');
    const [sortOrder, setSortOrder] = useState('asc');

    // Filter tables (search only, role and module are handled by backend)
    let filteredTables = tables?.filter(table => {
        const matchesSearch = table.tableName.toLowerCase().includes(searchText.toLowerCase());
        const matchesRole = roleFilter === 'All' || table.role === roleFilter;
        return matchesSearch && matchesRole;
    }) || [];

    // Sort tables
    filteredTables = [...filteredTables].sort((a, b) => {
        // Pinned tables always first
        const aPinned = pinnedTables.includes(a.tableName);
        const bPinned = pinnedTables.includes(b.tableName);
        if (aPinned && !bPinned) return -1;
        if (!aPinned && bPinned) return 1;

        // Then sort by selected criteria
        let comparison = 0;
        if (sortBy === 'name') {
            comparison = a.tableName.localeCompare(b.tableName);
        } else if (sortBy === 'rows') {
            comparison = (a.rowCount || 0) - (b.rowCount || 0);
        } else if (sortBy === 'columns') {
            comparison = (a.columnCount || 0) - (b.columnCount || 0);
        }

        return sortOrder === 'asc' ? comparison : -comparison;
    });

    const toggleSort = (field) => {
        if (sortBy === field) {
            setSortOrder(sortOrder === 'asc' ? 'desc' : 'asc');
        } else {
            setSortBy(field);
            setSortOrder('asc');
        }
    };

    return (
        <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
            {/* Filters */}
            <div style={{ padding: '16px', borderBottom: '1px solid #f0f0f0' }}>
                <Space direction="vertical" style={{ width: '100%' }} size="middle">
                    <Search
                        placeholder="Search tables..."
                        prefix={<SearchOutlined />}
                        value={searchText}
                        onChange={(e) => setSearchText(e.target.value)}
                        allowClear
                    />

                    {/* Active Module Filter Badge */}
                    {moduleFilter && (
                        <Alert
                            message={`Filtering by module: ${moduleFilter}`}
                            type="info"
                            showIcon
                            closable={false}
                            style={{ padding: '4px 12px', fontSize: 12 }}
                        />
                    )}

                    {/* Role Tabs */}
                    <Segmented
                        options={['All', 'Master', 'Transaction', 'Bridge', 'Configuration', 'LogAudit']}
                        value={roleFilter}
                        onChange={setRoleFilter}
                        block
                        size="small"
                    />

                    {/* Sort Options */}
                    <Space style={{ width: '100%', justifyContent: 'space-between' }}>
                        <span style={{ fontSize: 12, color: '#999' }}>Sort by:</span>
                        <Space size="small">
                            <Button
                                size="small"
                                type={sortBy === 'name' ? 'primary' : 'default'}
                                onClick={() => toggleSort('name')}
                                icon={sortBy === 'name' && (sortOrder === 'asc' ? <SortAscendingOutlined /> : <SortDescendingOutlined />)}
                            >
                                Name
                            </Button>
                            <Button
                                size="small"
                                type={sortBy === 'rows' ? 'primary' : 'default'}
                                onClick={() => toggleSort('rows')}
                                icon={sortBy === 'rows' && (sortOrder === 'asc' ? <SortAscendingOutlined /> : <SortDescendingOutlined />)}
                            >
                                Rows
                            </Button>
                            <Button
                                size="small"
                                type={sortBy === 'columns' ? 'primary' : 'default'}
                                onClick={() => toggleSort('columns')}
                                icon={sortBy === 'columns' && (sortOrder === 'asc' ? <SortAscendingOutlined /> : <SortDescendingOutlined />)}
                            >
                                Cols
                            </Button>
                        </Space>
                    </Space>
                </Space>
            </div>

            {/* Table List */}
            <div style={{ flex: 1, overflow: 'auto' }}>
                {loading ? (
                    <div style={{ textAlign: 'center', padding: '40px 0' }}>
                        <Spin />
                    </div>
                ) : filteredTables.length === 0 ? (
                    <Empty
                        image={Empty.PRESENTED_IMAGE_SIMPLE}
                        description="No tables found"
                        style={{ marginTop: 40 }}
                    />
                ) : (
                    <List
                        dataSource={filteredTables}
                        pagination={{
                            pageSize: 10,
                            size: 'small',
                            align: 'center',
                            hideOnSinglePage: true
                        }}
                        renderItem={(table) => {
                            const isPinned = pinnedTables.includes(table.tableName);
                            return (
                                <List.Item
                                    key={table.tableName}
                                    onClick={() => onTableSelect(table)}
                                    style={{
                                        cursor: 'pointer',
                                        backgroundColor: selectedTable?.tableName === table.tableName ? '#e6f7ff' : 'transparent',
                                        padding: '12px 16px',
                                        borderLeft: selectedTable?.tableName === table.tableName ? '3px solid #1890ff' : '3px solid transparent',
                                    }}
                                    className="table-list-item"
                                >
                                    <List.Item.Meta
                                        avatar={
                                            <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                                                {isPinned && <StarFilled style={{ color: '#faad14', fontSize: 12 }} />}
                                                <div
                                                    style={{
                                                        width: 8,
                                                        height: 8,
                                                        borderRadius: '50%',
                                                        backgroundColor: ROLE_COLORS[table.role] || '#d9d9d9',
                                                    }}
                                                />
                                            </div>
                                        }
                                        title={
                                            <Space>
                                                <TableOutlined style={{ fontSize: 14 }} />
                                                <span style={{ fontWeight: 500 }}>{table.tableName}</span>
                                            </Space>
                                        }
                                        description={
                                            <Space direction="vertical" size={2} style={{ width: '100%' }}>
                                                <div>
                                                    <Tag color={ROLE_COLORS[table.role]} style={{ fontSize: 11, padding: '0 4px' }}>
                                                        {ROLE_LABELS[table.role]}
                                                    </Tag>
                                                    {table.module && (
                                                        <Tag style={{ fontSize: 11, padding: '0 4px' }}>
                                                            {table.module}
                                                        </Tag>
                                                    )}
                                                </div>
                                                <div style={{ fontSize: 12, color: '#999' }}>
                                                    {formatNumber(table.rowCount)} rows · {table.columnCount} cols
                                                </div>
                                            </Space>
                                        }
                                    />
                                </List.Item>
                            );
                        }}
                    />
                )}
            </div>

            <style jsx>{`
        .table-list-item:hover {
          background-color: #f5f5f5 !important;
        }
      `}</style>
        </div>
    );
};

export default TableList;
