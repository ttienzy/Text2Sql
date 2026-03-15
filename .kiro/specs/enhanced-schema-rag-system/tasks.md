# Implementation Plan: Enhanced Schema RAG System

## Overview

This implementation plan breaks down the enhanced schema RAG system into discrete coding tasks. The system adds automatic schema indexing on database connection, context-rich chunking strategies, and hybrid retrieval combining vector search, keyword matching, and graph traversal. Tasks are organized to build incrementally, with early validation through testing.

## Tasks

- [x] 1. Set up core data models and configuration
  - Create SchemaFingerprint, ConnectionResult, and ScoredSchemaElement models
  - Add new properties to RAGConfig (EnableHybridSearch, VectorWeight, KeywordWeight, GraphWeight)
  - Add new properties to QdrantConfig (EmbeddingModel, VectorSize)
  - Update RetrievedSchemaContext with RetrievalStrategies and ElementScores properties
  - _Requirements: 4.1-4.6, 7.1-7.2_

- [ ]* 1.1 Write unit tests for data models
  - Test SchemaFingerprint serialization and equality
  - Test ConnectionResult validation
  - Test ScoredSchemaElement score calculations
  - _Requirements: 4.1-4.6, 7.1-7.2_

- [x] 2. Implement schema fingerprint computation in Agent Orchestrator
  - [x] 2.1 Add ComputeSchemaFingerprint method to AgentOrchestrator
    - Create deterministic hash from table names, column names with types, and relationships
    - Use SHA256 for hash generation
    - Sort elements before hashing for consistency
    - _Requirements: 8.1_

  - [ ]* 2.2 Write property test for schema fingerprint computation
    - **Property 23: Schema Fingerprint Computation**
    - **Validates: Requirements 8.1**
    - Test that identical schemas produce identical fingerprints
    - Test that different schemas produce different fingerprints
    - Test determinism (same schema always produces same fingerprint)

- [x] 3. Enhance Qdrant Service with fingerprint storage and retrieval
  - [x] 3.1 Add methods to store and retrieve schema fingerprint metadata
    - Implement StoreSchemaFingerprintAsync method
    - Implement GetStoredFingerprintAsync method
    - Store fingerprint in collection metadata or as special point
    - _Requirements: 8.2_

  - [ ]* 3.2 Write property test for fingerprint storage
    - **Property 24: Fingerprint Storage**
    - **Validates: Requirements 8.2**
    - Test that stored fingerprints can be retrieved correctly
    - Test fingerprint persistence across operations

  - [x] 3.3 Add collection dimension validation
    - Implement ValidateCollectionDimensionAsync method
    - Check configured dimension matches collection dimension before operations
    - Return validation result with error details
    - _Requirements: 7.3, 7.6_

  - [ ]* 3.4 Write property test for dimension validation
    - **Property 21: Collection Dimension Validation**
    - **Validates: Requirements 7.3, 7.6**
    - Test validation passes when dimensions match
    - Test validation fails when dimensions mismatch

- [x] 4. Implement automatic schema indexing in Agent Orchestrator
  - [x] 4.1 Create ConnectToDatabaseAsync method
    - Extract database name from connection string
    - Set collection name using normalized database name
    - Check if collection exists using Qdrant Service
    - Get point count if collection exists
    - _Requirements: 1.1, 1.2, 4.4, 4.5_

  - [x] 4.2 Add indexing decision logic
    - If collection missing or empty, trigger indexing
    - If collection exists with points, compare fingerprints
    - If fingerprints match, skip indexing and use existing embeddings
    - If fingerprints differ, trigger re-indexing
    - _Requirements: 1.3, 1.6, 8.3, 8.4_

  - [x] 4.3 Add logging for indexing operations
    - Log when indexing starts with database name
    - Log when indexing is skipped with point count
    - Log when schema changes detected
    - Log indexing completion with metrics (count, duration)
    - Log errors with stack traces
    - _Requirements: 5.1, 5.3, 5.4, 5.5, 5.6, 8.6_

  - [ ]* 4.4 Write property test for automatic indexing trigger
    - **Property 1: Automatic Indexing Trigger**
    - **Validates: Requirements 1.1, 1.2, 1.3**
    - Test indexing triggered when collection missing
    - Test indexing triggered when point count is zero

  - [ ]* 4.5 Write property test for indexing skip optimization
    - **Property 3: Indexing Skip Optimization**
    - **Validates: Requirements 1.6**
    - Test indexing skipped when collection exists with points and fingerprint matches

  - [ ]* 4.6 Write property test for change-triggered re-indexing
    - **Property 26: Change-Triggered Re-indexing**
    - **Validates: Requirements 8.4**
    - Test re-indexing triggered when fingerprints don't match

- [x] 5. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Enhance Schema Indexer with context-rich chunking
  - [x] 6.1 Implement InferTablePurpose helper method
    - Use naming patterns to infer table purpose
    - Handle common table name patterns (orders, customers, products, users)
    - Return generic description for unknown patterns
    - _Requirements: 2.7_

  - [x] 6.2 Implement InferColumnPurpose helper method
    - Use naming patterns to infer column purpose
    - Handle common column patterns (id, name, email, date, amount)
    - Return generic description for unknown patterns
    - _Requirements: 2.7_

  - [x] 6.3 Update BuildSchemaDocuments for table-level chunks
    - Include table name, description (or inferred purpose)
    - Include all column names with types and constraints (PK, FK)
    - Include all foreign key relationships involving the table
    - Format as: "Table: {name}, Description: {description}, Columns: {column_list}, Relationships: {fk_relationships}"
    - _Requirements: 2.1, 2.4_

  - [ ]* 6.4 Write property test for table chunk completeness
    - **Property 5: Table Chunk Completeness**
    - **Validates: Requirements 2.1**
    - Test all tables have chunks with name, description, columns, and relationships

  - [x] 6.5 Update BuildSchemaDocuments for column-level chunks
    - Include fully qualified column name (table.column)
    - Include data type and description (or inferred purpose)
    - Include parent table context
    - Format as: "Column: {table}.{column}, Description: {description}, Type: {data_type}, Table: {table_context}"
    - _Requirements: 2.2, 2.5_

  - [ ]* 6.6 Write property test for column chunk completeness
    - **Property 6: Column Chunk Completeness**
    - **Validates: Requirements 2.2**
    - Test all columns have chunks with qualified name, type, description, and table context

  - [x] 6.7 Implement GenerateRelationshipDescription method
    - Use naming patterns to infer relationship semantics
    - Detect common patterns (foreign key naming conventions)
    - Support one-to-many, many-to-one patterns
    - Use generic template when inference fails
    - _Requirements: 9.1, 9.2, 9.3, 9.6_

  - [x] 6.8 Update BuildSchemaDocuments for relationship-level chunks
    - Include both table names and column names
    - Include semantic description from GenerateRelationshipDescription
    - Format as: "Relationship: {source_table}.{source_column} → {target_table}.{target_column}, Meaning: {semantic_description}"
    - _Requirements: 2.3, 2.6_

  - [ ]* 6.9 Write property test for relationship chunk completeness
    - **Property 7: Relationship Chunk Completeness**
    - **Validates: Requirements 2.3**
    - Test all relationships have chunks with both tables, columns, and semantic description

  - [ ]* 6.10 Write property test for description generation fallback
    - **Property 8: Description Generation Fallback**
    - **Validates: Requirements 2.7**
    - Test that elements without descriptions get generated descriptions

  - [ ]* 6.11 Write property test for relationship semantic description
    - **Property 30: Relationship Semantic Description**
    - **Validates: Requirements 9.1, 9.2**
    - Test semantic descriptions generated from naming patterns

- [x] 7. Implement comprehensive schema scanning
  - [x] 7.1 Update IndexSchemaAsync to scan all schema elements
    - Ensure all tables, columns, and relationships are scanned
    - Create embeddings for all schema chunks
    - Store embeddings with metadata in Qdrant
    - Store schema fingerprint with embeddings
    - _Requirements: 1.4, 1.5, 8.2_

  - [ ]* 7.2 Write property test for comprehensive schema scanning
    - **Property 2: Comprehensive Schema Scanning**
    - **Validates: Requirements 1.4, 1.5**
    - Test that all tables, columns, and relationships are indexed

  - [x] 7.3 Add error handling for indexing failures
    - Catch and log errors during schema scanning
    - Catch and log errors during embedding generation
    - Catch and log errors during vector store operations
    - Return ConnectionResult with error details
    - _Requirements: 1.7_

  - [ ]* 7.4 Write property test for indexing error handling
    - **Property 4: Indexing Error Handling**
    - **Validates: Requirements 1.7**
    - Test errors are logged and descriptive messages returned

- [x] 8. Implement re-indexing with collection cleanup
  - [x] 8.1 Create ReindexSchemaAsync method
    - Delete existing collection before re-indexing
    - Call IndexSchemaAsync with new schema and fingerprint
    - Log re-indexing operation
    - _Requirements: 8.5_

  - [ ]* 8.2 Write property test for re-indexing collection cleanup
    - **Property 27: Re-indexing Collection Cleanup**
    - **Validates: Requirements 8.5**
    - Test existing collection deleted before creating new embeddings

  - [x] 8.3 Add force re-index flag support
    - Add forceReindex parameter to ConnectToDatabaseAsync
    - Trigger re-indexing when flag is true regardless of fingerprint
    - _Requirements: 8.7_

  - [ ]* 8.4 Write property test for force re-index override
    - **Property 29: Force Re-index Override**
    - **Validates: Requirements 8.7**
    - Test re-indexing triggered when force flag is true

- [ ] 9. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Implement query embedding caching in Schema Retriever
  - [x] 10.1 Add IMemoryCache dependency to SchemaRetriever
    - Inject IMemoryCache in constructor
    - Configure cache size limit (1000 entries)
    - _Requirements: 10.2, 10.5_

  - [x] 10.2 Implement GetOrGenerateQueryEmbeddingAsync method
    - Check cache for existing embedding using query text as key
    - Return cached embedding if found and less than 1 hour old
    - Generate new embedding if cache miss
    - Store new embedding in cache with 1 hour expiration
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 10.3 Add cache fallback for embedding failures
    - Catch embedding generation errors
    - Check cache for any previous embedding (ignore age)
    - Use stale cached embedding if available
    - Log warning when using stale cache
    - _Requirements: 10.6_

  - [ ]* 10.4 Write property test for query embedding caching
    - **Property 33: Query Embedding Caching**
    - **Validates: Requirements 10.1, 10.2, 10.3**
    - Test cached embeddings reused within 1 hour
    - Test new embeddings generated after cache miss

  - [ ]* 10.5 Write property test for cache expiration and size limits
    - **Property 34: Cache Expiration and Size Limits**
    - **Validates: Requirements 10.4, 10.5**
    - Test entries older than 1 hour evicted
    - Test LRU eviction when cache exceeds 1000 entries

- [x] 11. Implement hybrid retrieval in Schema Retriever
  - [x] 11.1 Update RetrieveAsync to perform vector similarity search
    - Get or generate query embedding with caching
    - Check if vector store is available
    - Perform vector search with configured TopK and MinimumScore
    - Add "vector" to retrieval strategies list
    - Handle vector search failures gracefully
    - _Requirements: 3.1, 6.1, 6.2_

  - [x] 11.2 Add keyword matching to RetrieveAsync
    - Call KeywordSchemaRetriever when hybrid search enabled
    - Add "keyword" to retrieval strategies list
    - _Requirements: 3.2_

  - [x] 11.3 Implement MergeResults method
    - Combine vector and keyword results
    - Create ScoredSchemaElement for each result
    - Deduplicate overlapping results
    - Preserve highest score for each unique element
    - _Requirements: 3.4, 3.6_

  - [ ]* 11.4 Write property test for result deduplication
    - **Property 11: Result Deduplication**
    - **Validates: Requirements 3.6**
    - Test overlapping results deduplicated with highest score preserved

  - [x] 11.5 Implement TraverseSchemaGraph method
    - Extract table names from seed results
    - Find relationships involving seed tables
    - Add related tables with graph score (0.5)
    - Add "graph" to retrieval strategies list
    - _Requirements: 3.3_

  - [x] 11.6 Implement RankByCombinedScore method
    - Calculate combined score using weighted formula
    - Use configured weights (vector: 0.5, keyword: 0.3, graph: 0.2)
    - Sort results by combined score descending
    - _Requirements: 3.5, 3.7_

  - [ ]* 11.7 Write property test for hybrid retrieval combination
    - **Property 9: Hybrid Retrieval Combination**
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
    - Test results combined from all enabled strategies

  - [ ]* 11.8 Write property test for weighted score ranking
    - **Property 10: Weighted Score Ranking**
    - **Validates: Requirements 3.5, 3.7**
    - Test results ranked by weighted combined score

  - [x] 11.9 Add retrieval strategy metadata to response
    - Set RetrievalStrategies property with list of used strategies
    - Set ElementScores dictionary with individual scores
    - _Requirements: 6.4_

  - [ ]* 11.10 Write property test for retrieval strategy metadata
    - **Property 18: Retrieval Strategy Metadata**
    - **Validates: Requirements 6.4**
    - Test response includes which strategies were used

- [x] 12. Implement graceful fallback for vector search failures
  - [ ] 12.1 Add try-catch around vector search in RetrieveAsync
    - Log vector search errors
    - Continue with keyword search and graph traversal
    - _Requirements: 6.1, 6.2, 6.3_

  - [ ]* 12.2 Write property test for vector search fallback
    - **Property 17: Vector Search Fallback**
    - **Validates: Requirements 6.1, 6.2, 6.3**
    - Test fallback to keyword search when vector search fails

  - [x] 12.3 Add error handling for all retrieval strategies failing
    - Return empty result set with error message
    - Do not throw unhandled exceptions
    - _Requirements: 6.5, 6.6_

  - [ ]* 12.4 Write property test for graceful retrieval failure
    - **Property 19: Graceful Retrieval Failure**
    - **Validates: Requirements 6.5, 6.6**
    - Test empty result with error message when all strategies fail

- [x] 13. Implement collection name consistency across components
  - [x] 13.1 Create shared collection name generation utility
    - Implement NormalizeCollectionName method
    - Format: "schema_embeddings_{normalized_database_name}"
    - Normalize by converting to lowercase and replacing special chars with underscores
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 13.2 Update all components to use shared utility
    - Update AgentOrchestrator to use NormalizeCollectionName
    - Update SchemaIndexer to use NormalizeCollectionName
    - Update SchemaRetriever to use NormalizeCollectionName
    - Update QdrantService to use NormalizeCollectionName
    - _Requirements: 4.1, 4.2, 4.3, 4.6_

  - [ ]* 13.3 Write property test for collection name consistency
    - **Property 12: Collection Name Consistency**
    - **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6**
    - Test all components generate identical collection names for same database

- [x] 14. Implement embedding configuration consistency
  - [ ] 14.1 Update SchemaIndexer to read embedding config
    - Read EmbeddingModel from QdrantConfig (default: "text-embedding-3-small")
    - Read VectorSize from QdrantConfig (default: 1536)
    - Pass config to EmbeddingClient
    - _Requirements: 7.1, 7.2_

  - [ ] 14.2 Update SchemaRetriever to use same embedding config
    - Read same EmbeddingModel and VectorSize from config
    - Ensure consistency with SchemaIndexer
    - _Requirements: 7.4_

  - [ ]* 14.3 Write property test for embedding configuration consistency
    - **Property 20: Embedding Configuration Consistency**
    - **Validates: Requirements 7.1, 7.2, 7.4**
    - Test SchemaIndexer and SchemaRetriever use same embedding config

  - [ ] 14.4 Add validation for configuration changes
    - Detect when embedding model or dimension changes
    - Log warning that re-indexing is required
    - Provide clear error message if dimension mismatch detected
    - _Requirements: 7.5_

  - [ ]* 14.5 Write property test for configuration change re-indexing
    - **Property 22: Configuration Change Re-indexing**
    - **Validates: Requirements 7.5**
    - Test system requires re-indexing when embedding config changes

- [ ] 15. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [ ] 16. Add comprehensive logging throughout the system
  - [ ] 16.1 Add progress logging to SchemaIndexer
    - Log when scanning tables starts
    - Log when creating embeddings starts
    - Log when storing in Qdrant starts
    - Include counts and timing information
    - _Requirements: 5.2_

  - [ ]* 16.2 Write unit test for indexing progress logging
    - Test progress messages emitted for major steps
    - _Requirements: 5.2_

  - [ ] 16.3 Add completion logging to AgentOrchestrator
    - Log success message with embedding count and duration
    - _Requirements: 5.3, 5.6_

  - [ ]* 16.4 Write property test for indexing completion logging
    - **Property 14: Indexing Completion Logging**
    - **Validates: Requirements 5.3, 5.6**
    - Test success message includes count and duration

  - [ ] 16.5 Add skip logging to AgentOrchestrator
    - Log when existing embeddings are used
    - Include point count in message
    - _Requirements: 5.4_

  - [ ]* 16.6 Write property test for indexing skip logging
    - **Property 15: Indexing Skip Logging**
    - **Validates: Requirements 5.4**
    - Test skip message includes point count

  - [ ] 16.7 Add error logging to AgentOrchestrator
    - Log errors with failure reason and stack trace
    - _Requirements: 5.5_

  - [ ]* 16.8 Write property test for indexing error logging
    - **Property 16: Indexing Error Logging**
    - **Validates: Requirements 5.5**
    - Test error messages include reason and stack trace

  - [ ] 16.9 Add schema change detection logging
    - Log when fingerprints don't match
    - Include details about what changed (tables, columns)
    - _Requirements: 8.6_

  - [ ]* 16.10 Write property test for schema change detection logging
    - **Property 28: Schema Change Detection Logging**
    - **Validates: Requirements 8.6**
    - Test change detection logs indicate what changed

- [ ] 17. Add integration tests for end-to-end scenarios
  - [ ]* 17.1 Write integration test for first-time connection
    - Test connect to new database triggers automatic indexing
    - Test successful retrieval after indexing
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [ ]* 17.2 Write integration test for reconnection with existing embeddings
    - Test reconnect skips indexing when embeddings exist
    - Test fingerprint comparison works correctly
    - _Requirements: 1.6, 8.3_

  - [ ]* 17.3 Write integration test for schema change detection
    - Test schema change triggers re-indexing
    - Test updated retrieval after re-indexing
    - _Requirements: 8.3, 8.4, 8.5_

  - [ ]* 17.4 Write integration test for hybrid retrieval
    - Test vector + keyword + graph strategies work together
    - Test result combination and ranking
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

  - [ ]* 17.5 Write integration test for fallback scenarios
    - Test Qdrant unavailable falls back to keyword search
    - Test graceful degradation
    - _Requirements: 6.1, 6.2, 6.3, 6.5, 6.6_

- [ ] 18. Update API endpoints and wire components together
  - [ ] 18.1 Update AgentController to use enhanced ConnectToDatabaseAsync
    - Pass connection string to ConnectToDatabaseAsync
    - Return ConnectionResult to client
    - Handle errors gracefully
    - _Requirements: 1.1, 1.2, 1.3_

  - [ ] 18.2 Update dependency injection configuration
    - Register IMemoryCache for query caching
    - Configure cache size limits
    - Register all new components
    - _Requirements: 10.2, 10.5_

  - [ ] 18.3 Update configuration files
    - Add new RAGConfig properties with defaults
    - Add new QdrantConfig properties with defaults
    - Document configuration options
    - _Requirements: 7.1, 7.2_

  - [ ]* 18.4 Write integration test for complete workflow
    - Test API endpoint → connection → indexing → retrieval
    - Test all components wired correctly
    - _Requirements: All_

- [ ] 19. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation at key milestones
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- Integration tests validate end-to-end scenarios
- The implementation uses C# with FsCheck for property-based testing
- All components follow the layered architecture defined in the design document
