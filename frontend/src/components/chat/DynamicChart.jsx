import React, { useMemo } from 'react';
import {
  ResponsiveContainer,
  LineChart, Line,
  BarChart, Bar,
  PieChart, Pie, Cell,
  ScatterChart, Scatter,
  XAxis, YAxis, CartesianGrid, Tooltip, Legend
} from 'recharts';
import { Empty, Typography } from 'antd';

const { Text } = Typography;

// Modern Enterprise Color Palette
const COLORS = ['#4f46e5', '#0ea5e9', '#10b981', '#f59e0b', '#ec4899', '#8b5cf6', '#14b8a6'];

const CustomTooltip = ({ active, payload, label }) => {
  if (active && payload && payload.length) {
    return (
      <div style={{
        backgroundColor: 'rgba(255, 255, 255, 0.95)',
        border: '1px solid #e5e7eb',
        borderRadius: '8px',
        padding: '12px',
        boxShadow: '0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06)',
        backdropFilter: 'blur(4px)',
      }}>
        <Text strong style={{ display: 'block', marginBottom: '8px', color: '#111827' }}>
          {label !== undefined ? label : payload[0].name}
        </Text>
        {payload.map((entry, index) => (
          <div key={`item-${index}`} style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '4px' }}>
            <span style={{
              width: '8px', height: '8px', borderRadius: '50%', backgroundColor: entry.color
            }} />
            <Text style={{ color: '#4b5563', fontSize: '13px' }}>
              {entry.name}: <span style={{ fontWeight: 600, color: '#111827' }}>
                {typeof entry.value === 'number' ? entry.value.toLocaleString() : entry.value}
              </span>
            </Text>
          </div>
        ))}
      </div>
    );
  }
  return null;
};

const formatAxisValue = (value) => {
  if (typeof value === 'number') {
    if (value >= 1000000000) return `${(value / 1000000000).toFixed(1)}B`;
    if (value >= 1000000) return `${(value / 1000000).toFixed(1)}M`;
    if (value >= 1000) return `${(value / 1000).toFixed(1)}K`;
    return value.toString();
  }
  // Truncate long strings for axis
  if (typeof value === 'string' && value.length > 15) {
    return value.substring(0, 15) + '...';
  }
  return value;
};

const DynamicChart = ({ data, type = 'line', height = 350 }) => {
  const chartConfig = useMemo(() => {
    if (!data || data.length === 0) return null;

    const firstItem = data[0];
    const keys = Object.keys(firstItem);
    
    // Auto-detect axes
    let xAxisKey = null;
    let yAxisKeys = [];

    // Identify types of columns
    const stringKeys = [];
    const numberKeys = [];

    keys.forEach(key => {
      // Check first few items to determine type confidently
      const isNumber = data.slice(0, 5).every(item => typeof item[key] === 'number' || !isNaN(parseFloat(item[key])));
      if (isNumber) {
        numberKeys.push(key);
      } else {
        stringKeys.push(key);
      }
    });

    // Strategy: First string column is X, all number columns are Y
    if (stringKeys.length > 0) {
      xAxisKey = stringKeys[0];
    } else if (keys.length > 0) {
      // If no strings, use first column as X, rest as Y
      xAxisKey = keys[0];
      numberKeys.shift(); // Remove first from number keys if it was put there
    }

    if (numberKeys.length > 0) {
      yAxisKeys = numberKeys;
    } else if (keys.length > 1) {
      // Fallback
      yAxisKeys = [keys[1]];
    }

    return { xAxisKey, yAxisKeys };
  }, [data]);

  if (!data || data.length === 0 || !chartConfig || chartConfig.yAxisKeys.length === 0) {
    return <Empty description="Insufficient data for visualization" />;
  }

  const { xAxisKey, yAxisKeys } = chartConfig;
  const normalizedType = type?.toLowerCase() || 'line';

  // Render Pie Chart
  if (normalizedType === 'pie') {
    const valueKey = yAxisKeys[0];
    const nameKey = xAxisKey || keys[0];
    
    return (
      <div style={{ width: '100%', height, paddingTop: '16px' }}>
        <ResponsiveContainer>
          <PieChart>
            <Pie
              data={data}
              dataKey={valueKey}
              nameKey={nameKey}
              cx="50%"
              cy="50%"
              outerRadius={height / 2.5}
              innerRadius={height / 4}
              paddingAngle={2}
              label={({ name, percent }) => `${name} ${(percent * 100).toFixed(0)}%`}
              labelLine={false}
              animationBegin={0}
              animationDuration={800}
            >
              {data.map((entry, index) => (
                <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
              ))}
            </Pie>
            <Tooltip content={<CustomTooltip />} />
            <Legend verticalAlign="bottom" height={36} iconType="circle" />
          </PieChart>
        </ResponsiveContainer>
      </div>
    );
  }

  // Common props for cartesian charts
  const cartesianProps = {
    data,
    margin: { top: 20, right: 30, left: 20, bottom: 20 }
  };

  const renderYAxes = () => {
    // If values are extremely large or small, Recharts might squish them without formatter
    return <YAxis tickFormatter={formatAxisValue} axisLine={false} tickLine={false} tick={{ fill: '#6b7280', fontSize: 12 }} />;
  };

  const renderXAxis = () => (
    <XAxis 
      dataKey={xAxisKey} 
      tickFormatter={formatAxisValue}
      axisLine={{ stroke: '#e5e7eb' }} 
      tickLine={false} 
      tick={{ fill: '#6b7280', fontSize: 12 }} 
      dy={10}
    />
  );

  // Render Bar Chart
  if (normalizedType === 'bar') {
    return (
      <div style={{ width: '100%', height, paddingTop: '16px' }}>
        <ResponsiveContainer>
          <BarChart {...cartesianProps}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
            {renderXAxis()}
            {renderYAxes()}
            <Tooltip content={<CustomTooltip />} cursor={{ fill: '#f9fafb' }} />
            <Legend verticalAlign="top" height={36} iconType="circle" />
            {yAxisKeys.map((key, idx) => (
              <Bar 
                key={key} 
                dataKey={key} 
                fill={COLORS[idx % COLORS.length]} 
                radius={[4, 4, 0, 0]}
                animationDuration={1000}
              />
            ))}
          </BarChart>
        </ResponsiveContainer>
      </div>
    );
  }

  // Render Scatter Chart (fallback logic if requested)
  if (normalizedType === 'scatter') {
    return (
      <div style={{ width: '100%', height, paddingTop: '16px' }}>
        <ResponsiveContainer>
          <ScatterChart {...cartesianProps}>
            <CartesianGrid strokeDasharray="3 3" stroke="#f3f4f6" />
            <XAxis dataKey={xAxisKey} type="category" tickFormatter={formatAxisValue} />
            <YAxis dataKey={yAxisKeys[0]} tickFormatter={formatAxisValue} />
            <Tooltip content={<CustomTooltip />} cursor={{ strokeDasharray: '3 3' }} />
            <Legend verticalAlign="top" height={36} iconType="circle" />
            {yAxisKeys.map((key, idx) => (
              <Scatter 
                key={key} 
                name={key} 
                dataKey={key} 
                fill={COLORS[idx % COLORS.length]} 
                animationDuration={1000}
              />
            ))}
          </ScatterChart>
        </ResponsiveContainer>
      </div>
    );
  }

  // Default: Render Line Chart
  return (
    <div style={{ width: '100%', height, paddingTop: '16px' }}>
      <ResponsiveContainer>
        <LineChart {...cartesianProps}>
          <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
          {renderXAxis()}
          {renderYAxes()}
          <Tooltip content={<CustomTooltip />} />
          <Legend verticalAlign="top" height={36} iconType="circle" />
          {yAxisKeys.map((key, idx) => (
            <Line
              key={key}
              type="monotone"
              dataKey={key}
              stroke={COLORS[idx % COLORS.length]}
              strokeWidth={3}
              dot={{ r: 4, strokeWidth: 2, fill: '#fff' }}
              activeDot={{ r: 6, strokeWidth: 0 }}
              animationDuration={1500}
            />
          ))}
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
};

export default DynamicChart;
