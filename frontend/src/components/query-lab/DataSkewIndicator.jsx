import React from 'react';
import { Alert, Progress, Tag, Tooltip } from 'antd';
import { WarningOutlined, InfoCircleOutlined } from '@ant-design/icons';

/**
 * DataSkewIndicator - Shows data skew warnings and recommendations
 * Displays when column statistics indicate high data skew
 * @param {Array} columnStats - Array of column statistics
 * @param {boolean} pspActive - Whether PSP (Parameter Sensitivity Plan) is active
 */
const DataSkewIndicator = ({ columnStats, pspActive }) => {
    if (!columnStats || columnStats.length === 0) {
        return null;
    }

    const getSkewColor = (skewLevel) => {
        switch (skewLevel) {
            case 'Extreme': return 'error';
            case 'High': return 'warning';
            case 'Moderate': return 'warning';
            case 'Low': return 'info';
            default: return 'success';
        }
    };

    const getSkewPercent = (skewFactor) => {
        return Math.round(skewFactor * 100);
    };

    const getSkewStatus = (skewLevel) => {
        switch (skewLevel) {
            case 'Extreme': return 'exception';
            case 'High': return 'exception';
            case 'Moderate': return 'active';
            default: return 'normal';
        }
    };

    // Filter only columns with moderate or higher skew
    const skewedColumns = columnStats.filter(stat =>
        ['Moderate', 'High', 'Extreme'].includes(stat.skewLevel)
    );

    if (skewedColumns.length === 0) {
        return null;
    }

    return (
        <div style={{ marginTop: 16 }}>
            <Alert
                message={
                    <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <WarningOutlined />
                        <strong>Data Skew Detected</strong>
                        <Tooltip title="Data skew occurs when values are not evenly distributed. This can affect index usage and query performance.">
                            <InfoCircleOutlined style={{ color: '#1890ff', cursor: 'help' }} />
                        </Tooltip>
                    </div>
                }
                description={
                    <div>
                        <p style={{ marginBottom: 12 }}>
                            The following columns have significant data skew. SQL Server may choose table scans
                            over index seeks for majority values due to high selectivity.
                        </p>

                        {skewedColumns.map((stat, index) => (
                            <div
                                key={index}
                                style={{
                                    marginBottom: 16,
                                    padding: 12,
                                    background: '#fff',
                                    borderRadius: 4,
                                    border: '1px solid #d9d9d9'
                                }}
                            >
                                <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 8 }}>
                                    <strong>{stat.tableName}.{stat.columnName}</strong>
                                    <Tag color={getSkewColor(stat.skewLevel)}>
                                        {stat.skewLevel} Skew
                                    </Tag>
                                    <span style={{ color: '#666', fontSize: 12 }}>
                                        {stat.totalRows.toLocaleString()} rows
                                    </span>
                                </div>

                                <Progress
                                    percent={getSkewPercent(stat.skewFactor)}
                                    status={getSkewStatus(stat.skewLevel)}
                                    format={percent => `${percent}% skew`}
                                    style={{ marginBottom: 8 }}
                                />

                                {stat.topValues && stat.topValues.length > 0 && (
                                    <div style={{ marginBottom: 8 }}>
                                        <div style={{ fontSize: 12, color: '#666', marginBottom: 4 }}>
                                            Top Values:
                                        </div>
                                        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                                            {stat.topValues.slice(0, 3).map((value, idx) => (
                                                <Tag key={idx} color="blue">
                                                    {value.value}: {value.percentage}%
                                                </Tag>
                                            ))}
                                        </div>
                                    </div>
                                )}

                                {stat.indexRecommendation && (
                                    <Alert
                                        message="DBA Recommendation"
                                        description={stat.indexRecommendation}
                                        type="info"
                                        showIcon
                                        icon={<InfoCircleOutlined />}
                                        style={{ marginTop: 8 }}
                                    />
                                )}

                                {/* High Skew Warning with PSP Awareness */}
                                {stat.skewFactor > 0.7 && (
                                    <Alert
                                        message="High Data Skew Warning"
                                        description={
                                            pspActive ? (
                                                <div>
                                                    <strong>SQL Server 2022 PSP (Parameter Sensitivity Plan) is active.</strong>
                                                    <p style={{ marginTop: 8, marginBottom: 0 }}>
                                                        PSP may handle this automatically by creating multiple execution plans
                                                        for different cardinality scenarios. Verify Query Store is enabled to
                                                        benefit from PSP optimization.
                                                    </p>
                                                </div>
                                            ) : (
                                                <div>
                                                    <p style={{ marginBottom: 8 }}>
                                                        Consider the following strategies for high data skew:
                                                    </p>
                                                    <ul style={{ marginBottom: 0, paddingLeft: 20 }}>
                                                        <li>Use filtered index for minority values</li>
                                                        <li>Add OPTION(OPTIMIZE FOR UNKNOWN) to prevent parameter sniffing</li>
                                                        <li>Consider partitioning for very large tables</li>
                                                        <li>Upgrade to SQL Server 2022 for PSP optimization</li>
                                                    </ul>
                                                </div>
                                            )
                                        }
                                        type={pspActive ? "info" : "warning"}
                                        showIcon
                                        style={{ marginTop: 8 }}
                                    />
                                )}

                                {/* Stale Statistics Warning */}
                                {stat.isStale && (
                                    <Alert
                                        message="Stale Statistics"
                                        description={stat.staleWarning || "Statistics are outdated. Run UPDATE STATISTICS to improve query performance."}
                                        type="warning"
                                        showIcon
                                        style={{ marginTop: 8 }}
                                    />
                                )}
                            </div>
                        ))}

                        <div style={{
                            marginTop: 12,
                            padding: 12,
                            background: '#e6f7ff',
                            borderRadius: 4,
                            border: '1px solid #91d5ff'
                        }}>
                            <strong>💡 Understanding Data Skew:</strong>
                            <ul style={{ marginTop: 8, marginBottom: 0, paddingLeft: 20 }}>
                                <li>
                                    <strong>Parameter Sniffing:</strong> SQL Server may cache execution plans
                                    based on first parameter value, causing performance issues with skewed data.
                                </li>
                                <li>
                                    <strong>Index Usage:</strong> Indexes are most effective for minority values.
                                    For majority values (&gt;70%), SQL Server often chooses table scans.
                                </li>
                                <li>
                                    <strong>Solutions:</strong> Consider filtered indexes, partitioning, or
                                    OPTION (RECOMPILE) for queries with skewed predicates.
                                </li>
                            </ul>
                        </div>
                    </div>
                }
                type="warning"
                showIcon
                style={{ marginBottom: 16 }}
            />
        </div>
    );
};

export default DataSkewIndicator;
