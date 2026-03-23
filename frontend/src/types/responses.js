/**
 * Unified Pipeline Response Types (JavaScript with JSDoc)
 * Matches backend UnifiedPipelineResponse structure
 * 
 * Backend enums:
 * - PipelineType: "Query", "Write", "Ddl", "Forbidden", "Reject"
 * - IntentCategory: "Query", "Insert", "Update", "DdlIndex", "DdlProcedure", "DdlAlter", "DdlView", "Forbidden", "OffTopic", "Unknown"
 */

// ═══════════════════════════════════════════════════════════════
// TYPE GUARDS - Runtime type checking
// ═══════════════════════════════════════════════════════════════

/**
 * Check if pipeline is Query
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isQueryPipeline = (response) => {
    return response?.pipeline === 'Query';
};

/**
 * Check if pipeline is Write (INSERT/UPDATE)
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isWritePipeline = (response) => {
    return response?.pipeline === 'Write';
};

/**
 * Check if pipeline is DDL
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isDdlPipeline = (response) => {
    return response?.pipeline === 'Ddl';
};

/**
 * Check if pipeline is Forbidden
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isForbiddenPipeline = (response) => {
    return response?.pipeline === 'Forbidden';
};

/**
 * Check if pipeline is Reject (off-topic or unknown)
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isRejectPipeline = (response) => {
    return response?.pipeline === 'Reject';
};

// Legacy type guards - check data properties

export const isQueryData = (data) => {
    return data && 'answer' in data;
};

export const isWriteData = (data) => {
    // Write operations have preview with sqlStatement or targetTable
    // DDL operations have preview with ddlScript - check for that instead
    return data && data.preview && 
        (data.preview.sqlStatement || data.preview.targetTable || data.preview.operationType === 'Insert' || data.preview.operationType === 'Update');
};

export const isDdlData = (data) => {
    return data && 'preview' in data && data.preview?.ddlScript !== undefined;
};

export const isForbiddenData = (data) => {
    return data && 'result' in data && data.result?.isBlocked === true;
};

export const isRejectionData = (data) => {
    return data && 'reason' in data && 'language' in data;
};

// ═══════════════════════════════════════════════════════════════
// INTENT TYPE HELPERS - Check IntentCategory
// ═══════════════════════════════════════════════════════════════

/**
 * Check if intent is Query
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isIntentQuery = (response) => {
    return response?.intent?.type === 'Query';
};

/**
 * Check if intent is Insert
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isIntentInsert = (response) => {
    return response?.intent?.type === 'Insert';
};

/**
 * Check if intent is Update
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isIntentUpdate = (response) => {
    return response?.intent?.type === 'Update';
};

/**
 * Check if intent is DDL (any type: Index, Procedure, Alter, View)
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isIntentDdl = (response) => {
    const intentType = response?.intent?.type;
    return intentType === 'DdlIndex' || intentType === 'DdlProcedure' || 
           intentType === 'DdlAlter' || intentType === 'DdlView';
};

/**
 * Check if intent is Forbidden
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isIntentForbidden = (response) => {
    return response?.intent?.type === 'Forbidden';
};

/**
 * Check if intent is OffTopic
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isIntentOffTopic = (response) => {
    return response?.intent?.type === 'OffTopic';
};

/**
 * Check if intent is Unknown
 * @param {Object} response - UnifiedPipelineResponse
 * @returns {boolean}
 */
export const isIntentUnknown = (response) => {
    return response?.intent?.type === 'Unknown';
};

// Export empty object for named imports compatibility
export const UnifiedPipelineResponse = {};
export const IntentSummary = {};
export const IPipelineData = {};
export const QueryPipelineData = {};
export const WritePipelineData = {};
export const DdlPipelineData = {};
export const ForbiddenPipelineData = {};
export const RejectionPipelineData = {};
