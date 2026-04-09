import { useState } from 'react';
import { Card, Input, Button, List, Tag, Space, Empty, Spin, Alert } from 'antd';
import { SearchOutlined, TableOutlined, ThunderboltOutlined, BulbOutlined } from '@ant-design/icons';
import { useSemanticSearchQuery } from '../../api/dbExplorer/queries';

const { Search } = Input;

const ROLE_COLORS = {
    Master: '#1890ff',
    Transaction: '#52c41a',
    Bridge: '#faad14',
    Configuration: '#722ed1',
    LogAudit: '#8c8c8c',
};

const EXAMPLE_QUERIES = [
    { text: 'tìm bảng khách hàng', label: 'Vietnamese' },
    { text: 'find order tables', label: 'English' },
    { text: 'KH', label: 'Abbreviation' },
    { text: 'product inventory', label: 'Concept' },
];

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

    const handleExampleClick = (query) => {
        setSearchQuery(query);
        handleSearch(query);
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
            {/* Search Input */}
            <Search
                placeholder="🔍 Tìm kiếm bảng (Vietnamese, English, abbreviation)..."
                enterButton={<SearchOutlined />}
                size="large"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                onSearch={handleSearch}
                loading={isLoading}
                allowClear
                style={{ marginBottom: 16 }}
            />

            {/* Example Queries */}
            <div style={{ marginBottom: 16 }}>
                <div style={{ marginBottom: 8, fontSize: 12, color: '#999' }}>
                    <BulbOutlined /> Ví dụ tìm kiếm:
                </div>
                <Space wrap size={[8, 8]}>
                    {EXAMPLE_QUERIES.map((example, index) => (
                        <Button
                            key={index}
                            size="small"
                            onClick={() => handleExampleClick(example.text)}
                        >
                            {example.text}
                        </Button>
                    ))}
                </Space>
            </div>

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
                    closable
                />
            )}

            {/* Results */}
            {searchResults && !isLoading && (
                <>
                    <div style={{ marginBottom: 12, fontWeight: 500 }}>
                        Found {searchResults.resultCount} result{searchResults.resultCount !== 1 ? 's' : ''} for "{searchResults.query}"
                    </div>

                    {searchResults.results.length === 0 ? (
                        <Empty
                            description="No tables found"
                            image={Empty.PRESENTED_IMAGE_SIMPLE}
                        />
                    ) : (
                        <List
                            size="small"
                            dataSource={searchResults.results}
                            renderItem={(result) => (
                                <List.Item
                                    style={{ cursor: 'pointer' }}
                                    onClick={() => handleTableClick(result)}
                                >
                                    <List.Item.Meta
                                        avatar={<TableOutlined style={{ fontSize: 20, color: '#1890ff' }} />}
                                        title={
                                            <Space>
                                                <span>{result.tableName}</span>
                                                <Tag color="green">
                                                    {(result.score * 100).toFixed(0)}%
                                                </Tag>
                                            </Space>
                                        }
                                        description={
                                            <div>
                                                <Space size={4} style={{ marginBottom: 4 }}>
                                                    <Tag color={ROLE_COLORS[result.role]}>
                                                        {result.role}
                                                    </Tag>
                                                    {result.module && (
                                                        <Tag color="purple">{result.module}</Tag>
                                                    )}
                                                </Space>
                                                {result.semanticTags && result.semanticTags.length > 0 && (
                                                    <div style={{ fontSize: 11, color: '#999' }}>
                                                        Tags: {result.semanticTags.slice(0, 8).join(', ')}
                                                        {result.semanticTags.length > 8 && ` +${result.semanticTags.length - 8} more`}
                                                    </div>
                                                )}
                                            </div>
                                        }
                                    />
                                </List.Item>
                            )}
                        />
                    )}
                </>
            )}

            {/* Help Text */}
            {!activeQuery && !isLoading && (
                <div style={{ marginTop: 16, padding: 12, background: '#f5f5f5', borderRadius: 4 }}>
                    <div style={{ fontSize: 12, color: '#666', marginBottom: 8 }}>
                        <BulbOutlined /> Semantic Search Features:
                    </div>
                    <ul style={{ margin: 0, paddingLeft: 20, fontSize: 12, color: '#666' }}>
                        <li>Search in Vietnamese: "tìm bảng khách hàng"</li>
                        <li>Search in English: "find customer tables"</li>
                        <li>Search by abbreviation: "KH", "NV", "SP"</li>
                        <li>Search by concept: "sales", "inventory", "CRM"</li>
                    </ul>
                </div>
            )}
        </Card>
    );
};

export default SemanticSearch;
