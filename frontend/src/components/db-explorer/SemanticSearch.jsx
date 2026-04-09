import { useState } from 'react';
import { Input, List, Tag, Space, Empty, Spin, Alert, Card } from 'antd';
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
        <Card
            title={
                <Space>
                    <ThunderboltOutlined style={{ color: '#1890ff' }} />
                    <span>Semantic Search</span>
                </Space>
            }
            size="small"
            style={style}
        >
            <Search
                placeholder="🔍 Tìm kiếm bảng (Vietnamese/English/Abbreviation)..."
                enterButton={<SearchOutlined />}
                size="large"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                onSearch={handleSearch}
                loading={isLoading}
                allowClear
                style={{ marginBottom: 16 }}
            />

            {/* Examples */}
            {!activeQuery && (
                <div style={{ marginBottom: 16, padding: 12, backgroundColor: '#f5f5f5', borderRadius: 4 }}>
                    <div style={{ fontSize: 12, color: '#666', marginBottom: 8 }}>
                        💡 Ví dụ tìm kiếm:
                    </div>
                    <Space wrap>
                        <Tag
                            style={{ cursor: 'pointer' }}
                            onClick={() => {
                                setSearchQuery('tìm bảng khách hàng');
                                setActiveQuery('tìm bảng khách hàng');
                            }}
                        >
                            tìm bảng khách hàng
                        </Tag>
                        <Tag
                            style={{ cursor: 'pointer' }}
                            onClick={() => {
                                setSearchQuery('find order tables');
                                setActiveQuery('find order tables');
                            }}
                        >
                            find order tables
                        </Tag>
                        <Tag
                            style={{ cursor: 'pointer' }}
                            onClick={() => {
                                setSearchQuery('KH');
                                setActiveQuery('KH');
                            }}
                        >
                            KH (abbreviation)
                        </Tag>
                        <Tag
                            style={{ cursor: 'pointer' }}
                            onClick={() => {
                                setSearchQuery('product inventory');
                                setActiveQuery('product inventory');
                            }}
                        >
                            product inventory
                        </Tag>
                    </Space>
                </div>
            )}

            {/* Loading */}
            {isLoading && (
                <div style={{ textAlign: 'center', padding: '20px 0' }}>
                    <Spin />
                    <div style={{ marginTop: 8, color: '#999' }}>Searching...</div>
                </div>
            )}

            {/* Error */}
            {error && (
                <Alert
                    message="Search Failed"
                    description={error.response?.data?.message || error.message}
                    type="error"
                    showIcon
                    style={{ marginBottom: 16 }}
                />
            )}

            {/* Results */}
            {searchResults && !isLoading && (
                <>
                    <div style={{ marginBottom: 8, fontSize: 12, color: '#999' }}>
                        Found {searchResults.resultCount} result{searchResults.resultCount !== 1 ? 's' : ''} for "{searchResults.query}"
                    </div>

                    {searchResults.results.length === 0 ? (
                        <Empty
                            description="No tables found"
                            image={Empty.PRESENTED_IMAGE_SIMPLE}
                            style={{ padding: '20px 0' }}
                        />
                    ) : (
                        <List
                            dataSource={searchResults.results}
                            renderItem={(result) => (
                                <List.Item
                                    style={{ cursor: 'pointer', padding: '12px 0' }}
                                    onClick={() => handleTableClick(result)}
                                    extra={
                                        <Tag color="green">
                                            {(result.score * 100).toFixed(0)}%
                                        </Tag>
                                    }
                                >
                                    <List.Item.Meta
                                        avatar={<TableOutlined style={{ fontSize: 20, color: '#1890ff' }} />}
                                        title={
                                            <Space>
                                                <span style={{ fontWeight: 500 }}>{result.tableName}</span>
                                                <Tag color={ROLE_COLORS[result.role]} style={{ fontSize: 11 }}>
                                                    {result.role}
                                                </Tag>
                                                {result.module && (
                                                    <Tag color="purple" style={{ fontSize: 11 }}>
                                                        📦 {result.module}
                                                    </Tag>
                                                )}
                                            </Space>
                                        }
                                        description={
                                            result.semanticTags && result.semanticTags.length > 0 && (
                                                <Space wrap style={{ marginTop: 4 }}>
                                                    {result.semanticTags.slice(0, 8).map((tag, idx) => (
                                                        <Tag key={idx} style={{ fontSize: 11, margin: '2px 0' }}>
                                                            {tag}
                                                        </Tag>
                                                    ))}
                                                    {result.semanticTags.length > 8 && (
                                                        <Tag style={{ fontSize: 11 }}>
                                                            +{result.semanticTags.length - 8} more
                                                        </Tag>
                                                    )}
                                                </Space>
                                            )
                                        }
                                    />
                                </List.Item>
                            )}
                        />
                    )}
                </>
            )}

            {/* Help text */}
            {!activeQuery && !isLoading && (
                <div style={{ marginTop: 16, padding: 12, backgroundColor: '#e6f7ff', borderRadius: 4, fontSize: 12 }}>
                    <div style={{ fontWeight: 500, marginBottom: 4, color: '#1890ff' }}>
                        💡 Semantic Search Features:
                    </div>
                    <ul style={{ margin: 0, paddingLeft: 20, color: '#666' }}>
                        <li>Search in Vietnamese: "tìm bảng đơn hàng"</li>
                        <li>Search in English: "find customer tables"</li>
                        <li>Search by abbreviation: "KH", "NV", "SP"</li>
                        <li>Search by concept: "sales", "inventory", "user management"</li>
                    </ul>
                </div>
            )}
        </Card>
    );
};

export default SemanticSearch;
