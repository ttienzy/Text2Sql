import { useQuery } from '@tanstack/react-query';
import api from '../axios';

// Query keys
export const queryOptimizerKeys = {
    all: ['queryOptimizer'],
    analyze: (connectionId, sql) => [...queryOptimizerKeys.all, 'analyze', connectionId, sql],
};

// No queries needed for now - only mutations
// This file is kept for future expansion (e.g., query history, saved optimizations)
