/**
 * API endpoint constants for intent-based pipelines
 */

export const API_ENDPOINTS = {
    // Agent endpoints
    AGENT: {
        PROCESS: '/api/agent/process',
        HEALTH: '/api/agent/health'
    },

    // WRITE pipeline endpoints
    WRITE: {
        PREVIEW: '/api/agent/write/preview',
        EXECUTE: '/api/agent/write/execute'
    },

    // DDL pipeline endpoints
    DDL: {
        PREVIEW: '/api/agent/ddl/preview',
        EXECUTE: '/api/agent/ddl/execute'
    },

    // Connection endpoints
    CONNECTIONS: {
        LIST: '/api/connections',
        GET: (id) => `/api/connections/${id}`,
        CREATE: '/api/connections',
        UPDATE: (id) => `/api/connections/${id}`,
        DELETE: (id) => `/api/connections/${id}`,
        TEST: '/api/connections/test'
    },

    // Conversation endpoints
    CONVERSATIONS: {
        LIST: '/api/conversations',
        GET: (id) => `/api/conversations/${id}`,
        CREATE: '/api/conversations',
        DELETE: (id) => `/api/conversations/${id}`
    },

    // Message endpoints
    MESSAGES: {
        LIST: (conversationId) => `/api/conversations/${conversationId}/messages`,
        GET: (conversationId, messageId) => `/api/conversations/${conversationId}/messages/${messageId}`
    }
};

/**
 * Pipeline types
 */
export const PIPELINE_TYPES = {
    QUERY: 'QUERY',
    WRITE: 'WRITE',
    DDL: 'DDL',
    FORBIDDEN: 'FORBIDDEN',
    REJECT: 'REJECT'
};

/**
 * Operation types
 */
export const OPERATION_TYPES = {
    // WRITE operations
    INSERT: 'Insert',
    UPDATE: 'Update',

    // DDL operations
    CREATE_INDEX: 'CreateIndex',
    ALTER_TABLE: 'AlterTable',
    CREATE_VIEW: 'CreateView',
    CREATE_PROCEDURE: 'CreateProcedure',

    // FORBIDDEN operations
    DELETE: 'Delete',
    DROP: 'Drop',
    TRUNCATE: 'Truncate'
};

/**
 * Safe alternative types
 */
export const SAFE_ALTERNATIVE_TYPES = {
    SOFT_DELETE: 'SoftDelete',
    ARCHIVE: 'Archive',
    INACTIVE_FLAG: 'InactiveFlag',
    AUDIT_LOG: 'AuditLog'
};

export default {
    API_ENDPOINTS,
    PIPELINE_TYPES,
    OPERATION_TYPES,
    SAFE_ALTERNATIVE_TYPES
};
