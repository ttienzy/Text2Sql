import { useState } from 'react';
import { Input, List, Tag, Space, Empty, Spin, Alert } from 'antd';
import { SearchOutlined, TableOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { useSemanticSearchQuery } from '../../api/dbExplorer/queries';

const { Search } = Input;

const ROLE_COLORS = {
    Master: '#1890ff',
    Transaction: '#52c41a',
    Bridge: '#faad14',
    Configuration: '#722ed1',
    LogAudit: '#8c8c8c',
};

const SemanticSearch = ({ connectionId, onTableSelect, style }) => {
    const [searchQuery, setSearchQuery] = useState('');
    const [activeQuery, setActiveQuery] = useState('');

    const { data: searchResults, isLoading, error } = useSemanticSearchQuery(
        connectionId,
        activeQuery,
        { enabled: activeQuery.length >= 2 }
    );

    const handleSearch = (value) => {
        const trimmed = value.trim();
        if (trimmed.length >= 2) {
            setActiveQuery(trimmed);
        }
    };

    const handleTableClick = (result) => {
        if (onTableSelect) {
            onTableSelect({
                tableName: result.tableName,
                role: result.role,
                module: result.module,
            });
        }
    };

    return (
        <div style={style}>
            <div style={{ marginBottom: 12 }}>
                <Space style={{ width: '100%', marginBottom: 8 }}>
                    <ThunderboltOutlined style={{ color: '#1890ff', fontSize: 16 }} />
                    <span style={{ fontWeight: 500, fontSize: 14 }}>Semantic Search</span>
                </Space>

                <Search
                    placeholder="🔍 Tìm kiếm bảng..."
                    enterButton={<SearchOutlined />}
                    size="middle"
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    onSearch={handleSearch}
                    loading={isLoading}
                    allowClear
                />
            </div>

            {/* Loading */}
            {isLoading && (
                <div style={{ textAlign: 'center', padding: '12px 0' }}>
                    <Spin size="small" />
                </div>
            )}

            {/* Error */}
            {error && (
                <Alert
                    message="Search Failed"
                    description={error.response?.data?.message || error.message}
                    type="error"
                    showIcon
                    style={{ marginBottom: 12, fontSize: 12 }}
                    closable
                />
            )}

            {/* Results */}
            {searchResults && !isLoading && (
                <>
                    <div style={{ marginBottom: 8, fontSize: 11, color: '#999' }}>
                        {searchResults.resultCount} result{searchResults.resultCount !== 1 ? 's' : ''}
                    </div>

                    {searchResults.results.length === 0 ? (
                        <Empty
                            description="No tables found"
                            image={Empty.PRESENTED_IMAGE_SIMPLE}
                            style={{ padding: '12px 0' }}
                        />
                    ) : (
                        <div style={{ maxHeight: '200px', overflow: 'auto' }}>
                            <List
                                size="small"
                                dataSource={searchResults.results}
                                renderItem={(result) => (
                                    <List.Item
                                        style={{ cursor: 'pointer', padding: '8px 0' }}
                                        onClick={() => handleTableClick(result)}
                                    >
                                        <List.Item.Meta
                                            avatar={<TableOutlined style={{ fontSize: 16, color: '#1890ff' }} />}
                                            title={
                                                <Space size={4}>
                                                    <span style={{ fontSize: 13 }}>{result.tableName}</span>
                                                    <Tag color="green" style={{ fontSize: 10, padding: '0 4px' }}>
                                                        {(result.score * 100).toFixed(0)}%
                                                    </Tag>
                                                </Space>
                                            }
                                            description={
                                                <Space size={4}>
                                                    <Tag color={ROLE_COLORS[result.role]} style={{ fontSize: 10, margin: 0 }}>
                                                        {result.role}
                                                    </Tag>
                                                    {result.module && (
                                                        <Tag color="purple" style={{ fontSize: 10, margin: 0 }}>
                                                            {result.module}
                                                        </Tag>
                                                    )}
                                                </Space>
                                            }
                                        />
                                    </List.Item>
                                )}
                            />
                        </div>
                    )}
                </>
            )}
        </div>
    );
};

export default SemanticSearch;
