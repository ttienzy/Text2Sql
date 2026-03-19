import React from 'react';
import { Link } from 'react-router-dom';
import { TableOutlined } from '@ant-design/icons';

/**
 * Detect table names in text and render as clickable links
 * Assumes table names are capitalized words or PascalCase
 * 
 * @param {string} text - The text to process
 * @param {Array<string>} tableNames - List of known table names from schema
 * @returns {React.ReactNode} - Text with table links
 */
export const renderTableLinks = (text, tableNames = []) => {
    if (!text || !tableNames || tableNames.length === 0) {
        return text;
    }

    // Create regex pattern from table names (case-insensitive)
    // Sort by length descending to match longer names first
    const sortedTableNames = [...tableNames].sort((a, b) => b.length - a.length);
    const pattern = sortedTableNames.map(name => name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|');
    const regex = new RegExp(`\\b(${pattern})\\b`, 'gi');

    const parts = [];
    let lastIndex = 0;
    let match;
    let keyIndex = 0;

    // Find all matches
    while ((match = regex.exec(text)) !== null) {
        // Add text before match
        if (match.index > lastIndex) {
            parts.push(text.substring(lastIndex, match.index));
        }

        // Add link for matched table name
        const tableName = match[0];
        parts.push(
            <Link
                key={`table-link-${keyIndex++}`}
                to="/explorer"
                state={{ selectedTable: tableName }}
                style={{
                    color: '#1890ff',
                    textDecoration: 'none',
                    borderBottom: '1px dashed #1890ff',
                    padding: '0 2px',
                }}
                onClick={(e) => {
                    // Prevent default to handle navigation manually if needed
                    // e.preventDefault();
                }}
            >
                <TableOutlined style={{ fontSize: 12, marginRight: 2 }} />
                {tableName}
            </Link>
        );

        lastIndex = regex.lastIndex;
    }

    // Add remaining text
    if (lastIndex < text.length) {
        parts.push(text.substring(lastIndex));
    }

    return parts.length > 0 ? parts : text;
};

/**
 * Extract table names mentioned in text
 * 
 * @param {string} text - The text to analyze
 * @param {Array<string>} tableNames - List of known table names
 * @returns {Array<string>} - List of detected table names
 */
export const extractTableNames = (text, tableNames = []) => {
    if (!text || !tableNames || tableNames.length === 0) {
        return [];
    }

    const detected = new Set();
    const sortedTableNames = [...tableNames].sort((a, b) => b.length - a.length);
    const pattern = sortedTableNames.map(name => name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|');
    const regex = new RegExp(`\\b(${pattern})\\b`, 'gi');

    let match;
    while ((match = regex.exec(text)) !== null) {
        // Find the actual table name (case-sensitive match)
        const matchedName = tableNames.find(
            name => name.toLowerCase() === match[0].toLowerCase()
        );
        if (matchedName) {
            detected.add(matchedName);
        }
    }

    return Array.from(detected);
};

/**
 * Check if text contains any table names
 * 
 * @param {string} text - The text to check
 * @param {Array<string>} tableNames - List of known table names
 * @returns {boolean} - True if any table names are found
 */
export const hasTableReferences = (text, tableNames = []) => {
    return extractTableNames(text, tableNames).length > 0;
};
