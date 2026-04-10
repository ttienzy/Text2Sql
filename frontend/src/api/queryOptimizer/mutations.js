import { useMutation, useQueryClient } from '@tanstack/react-query';
import api from '../axios';
import { queryOptimizerKeys } from './queries';

/**
 * Optimize SQL query
 */
export const useOptimizeQueryMutation = ({ onSuccess, onError } = {}) => {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async ({ sql, connectionId, includeExecutionPlan = false }) => {
            const response = await api.post('/api/query-optimizer/analyze', {
                sql,
                connectionId,
                includeExecutionPlan,
            });
            return response.data;
        },
        onSuccess: (data, variables) => {
            // Invalidate related queries if needed
            queryClient.invalidateQueries({ queryKey: queryOptimizerKeys.all });

            if (onSuccess) {
                onSuccess(data, variables);
            }
        },
        onError: (error, variables) => {
            if (onError) {
                onError(error, variables);
            }
        },
    });
};

/**
 * Optimize SQL query with execution plan comparison (Sprint 2)
 */
export const useOptimizeQueryWithPlanMutation = ({ onSuccess, onError } = {}) => {
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: async ({ sql, connectionId }) => {
            const response = await api.post('/api/query-optimizer/analyze-with-plan', {
                sql,
                connectionId,
            });
            return response.data;
        },
        onSuccess: (data, variables) => {
            // Invalidate related queries if needed
            queryClient.invalidateQueries({ queryKey: queryOptimizerKeys.all });

            if (onSuccess) {
                onSuccess(data, variables);
            }
        },
        onError: (error, variables) => {
            if (onError) {
                onError(error, variables);
            }
        },
    });
};
