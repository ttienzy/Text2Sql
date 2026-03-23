import { useState, useCallback } from 'react';
import axios from '../api/axios';

/**
 * Hook for paginated query results (lazy loading)
 * @param {string} resultId - Cached result ID from initial query
 * @param {number} initialPageSize - Page size (default: 50)
 * @returns {Object} Pagination state and actions
 */
export const useQueryPagination = (resultId, initialPageSize = 50) => {
    const [pages, setPages] = useState(new Map());  // Map<pageNumber, rows>
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);
    const [currentPage, setCurrentPage] = useState(1);
    const [hasMore, setHasMore] = useState(true);
    const [totalRows, setTotalRows] = useState(0);
    const [totalPages, setTotalPages] = useState(0);

    /**
     * Load a specific page
     */
    const loadPage = useCallback(async (page) => {
        if (!resultId) {
            console.warn('[useQueryPagination] No resultId provided');
            return null;
        }

        // Check if page already loaded
        if (pages.has(page)) {
            console.log('[useQueryPagination] Page', page, 'already loaded from cache');
            setCurrentPage(page);
            return pages.get(page);
        }

        setLoading(true);
        setError(null);

        try {
            const response = await axios.get(`/api/query-results/${resultId}`, {
                params: { page, pageSize: initialPageSize }
            });

            const { data } = response.data;

            // Update state
            setPages(prev => new Map(prev).set(page, data.rows));
            setCurrentPage(page);
            setHasMore(data.hasMore);
            setTotalRows(data.totalRows);
            setTotalPages(data.totalPages);

            console.log(
                '[useQueryPagination] Loaded page', page, '/', data.totalPages,
                'with', data.rows.length, 'rows'
            );

            return data.rows;
        } catch (err) {
            const errorMsg = err.response?.data?.message || 'Failed to load page';
            setError(errorMsg);
            console.error('[useQueryPagination] Error loading page:', err);
            return null;
        } finally {
            setLoading(false);
        }
    }, [resultId, initialPageSize, pages]);

    /**
     * Load next page
     */
    const loadNext = useCallback(async () => {
        if (!hasMore || loading) return null;
        return await loadPage(currentPage + 1);
    }, [hasMore, loading, currentPage, loadPage]);

    /**
     * Load previous page
     */
    const loadPrevious = useCallback(async () => {
        if (currentPage <= 1 || loading) return null;
        return await loadPage(currentPage - 1);
    }, [currentPage, loading, loadPage]);

    /**
     * Get all loaded rows (for display)
     */
    const getAllLoadedRows = useCallback(() => {
        const allRows = [];
        for (let i = 1; i <= currentPage; i++) {
            if (pages.has(i)) {
                allRows.push(...pages.get(i));
            }
        }
        return allRows;
    }, [pages, currentPage]);

    /**
     * Reset pagination state
     */
    const reset = useCallback(() => {
        setPages(new Map());
        setCurrentPage(1);
        setHasMore(true);
        setTotalRows(0);
        setTotalPages(0);
        setError(null);
    }, []);

    return {
        // State
        pages,
        loading,
        error,
        currentPage,
        hasMore,
        totalRows,
        totalPages,

        // Actions
        loadPage,
        loadNext,
        loadPrevious,
        getAllLoadedRows,
        reset,
    };
};

export default useQueryPagination;
