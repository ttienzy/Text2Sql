# Requirements Document

## Introduction

This document specifies requirements for enhancing the schema embedding and retrieval system in a Text-to-SQL RAG (Retrieval-Augmented Generation) application. The current system fails to automatically index database schemas after connection, uses overly granular chunking that lacks context, and relies on single-mode retrieval instead of hybrid search. These enhancements will improve semantic search accuracy from approximately 70% to 90% by implementing automatic schema indexing, context-rich chunking strategies, and hybrid retrieval combining vector search, keyword matching, and schema graph traversal.

## Glossary

- **Schema_Indexer**: Component responsible for scanning database schemas and creating embeddings in the vector database
- **Schema_Retriever**: Component responsible for retrieving relevant schema information using search strategies
- **Qdrant_Service**: Client service for interacting with the Qdrant vector database REST API
- **Agent_Orchestrator**: Main orchestration component that coordinates database connections and query processing
- **Collection**: A named set of vector embeddings stored in Qdrant
- **Schema_Chunk**: A semantically meaningful unit of schema information prepared for embedding
- **Hybrid_Retrieval**: Search strategy combining vector similarity, keyword matching, and graph traversal
- **Point_Count**: Number of vector embeddings stored in a Qdrant collection
- **Embedding_Model**: OpenAI text-embedding-3-small model producing 1536-dimensional vectors

## Requirements

### Requirement 1: Automatic Schema Indexing After Database Connection

**User Story:** As a developer using the Text-to-SQL system, I want the schema to be automatically indexed when I connect to a database, so that vector search works immediately without manual intervention.

#### Acceptance Criteria

1. WHEN a database connection is established, THE Agent_Orchestrator SHALL check if the corresponding Qdrant collection exists
2. WHEN the collection exists, THE Agent_Orchestrator SHALL retrieve the point count from the collection
3. IF the collection does not exist OR the point count equals zero, THEN THE Agent_Orchestrator SHALL trigger the Schema_Indexer to index the database schema
4. WHEN schema indexing is triggered, THE Schema_Indexer SHALL scan all tables, columns, and relationships from the connected database
5. WHEN schema indexing completes successfully, THE Schema_Indexer SHALL confirm that embeddings are stored in Qdrant
6. IF the collection exists AND the point count is greater than zero, THEN THE Agent_Orchestrator SHALL skip indexing and use existing embeddings
7. WHEN schema indexing fails, THE Agent_Orchestrator SHALL log the error and notify the user with a descriptive error message

### Requirement 2: Context-Rich Schema Chunking Strategy

**User Story:** As a developer building Text-to-SQL queries, I want schema information to be chunked with sufficient context, so that semantic search returns relevant schema elements accurately.

#### Acceptance Criteria

1. WHEN creating table-level chunks, THE Schema_Indexer SHALL include the table name, description, all column names with types, and all foreign key relationships in a single chunk
2. WHEN creating column-level chunks, THE Schema_Indexer SHALL include the fully qualified column name, data type, description, and parent table context
3. WHEN creating relationship-level chunks, THE Schema_Indexer SHALL include both table names, the foreign key column names, and a semantic description of the relationship meaning
4. THE Schema_Indexer SHALL format table chunks as: "Table: {name}, Description: {description}, Columns: {column_list_with_types}, Relationships: {fk_relationships}"
5. THE Schema_Indexer SHALL format column chunks as: "Column: {table}.{column}, Description: {description}, Type: {data_type}, Table: {table_context}"
6. THE Schema_Indexer SHALL format relationship chunks as: "Relationship: {source_table}.{source_column} → {target_table}.{target_column}, Meaning: {semantic_description}"
7. WHEN a schema element lacks a description, THE Schema_Indexer SHALL generate a basic semantic description based on naming conventions and context

### Requirement 3: Hybrid Retrieval Strategy

**User Story:** As a developer querying the Text-to-SQL system, I want the system to use multiple retrieval strategies, so that I get the most relevant schema information for my natural language query.

#### Acceptance Criteria

1. WHEN retrieving schema information, THE Schema_Retriever SHALL perform vector similarity search using the embedded query
2. WHEN retrieving schema information, THE Schema_Retriever SHALL perform keyword matching against table names, column names, and descriptions
3. WHEN retrieving schema information, THE Schema_Retriever SHALL traverse the schema graph to include related tables connected by foreign keys
4. THE Schema_Retriever SHALL combine results from vector search, keyword matching, and graph traversal into a unified result set
5. THE Schema_Retriever SHALL rank combined results using a weighted scoring algorithm that considers vector similarity score, keyword match count, and relationship proximity
6. WHEN multiple retrieval strategies return overlapping results, THE Schema_Retriever SHALL deduplicate schema elements while preserving the highest relevance score
7. THE Schema_Retriever SHALL return results ordered by combined relevance score in descending order

### Requirement 4: Collection Name Consistency

**User Story:** As a developer maintaining the Text-to-SQL system, I want collection names to be consistent across all components, so that schema embeddings are reliably found and retrieved.

#### Acceptance Criteria

1. THE Schema_Indexer SHALL generate collection names using the format "schema_embeddings_{database_name}"
2. THE Schema_Retriever SHALL generate collection names using the format "schema_embeddings_{database_name}"
3. THE Qdrant_Service SHALL use the same collection name format "schema_embeddings_{database_name}" for all operations
4. WHEN generating collection names, THE system SHALL normalize database names by converting to lowercase and replacing special characters with underscores
5. THE Agent_Orchestrator SHALL pass the normalized database name to all components requiring collection name generation
6. FOR ALL collection name generation operations, the format and normalization rules SHALL produce identical collection names for the same database

### Requirement 5: Schema Indexing Status Visibility

**User Story:** As a developer using the Text-to-SQL system, I want to see the status of schema indexing operations, so that I understand when the system is ready for queries.

#### Acceptance Criteria

1. WHEN schema indexing begins, THE Agent_Orchestrator SHALL log a message indicating indexing has started with the database name
2. WHILE schema indexing is in progress, THE Schema_Indexer SHALL log progress messages for each major step (scanning tables, creating embeddings, storing in Qdrant)
3. WHEN schema indexing completes successfully, THE Agent_Orchestrator SHALL log a success message with the total number of embeddings created
4. WHEN schema indexing is skipped because embeddings exist, THE Agent_Orchestrator SHALL log a message indicating existing embeddings are being used with the point count
5. IF schema indexing fails, THEN THE Agent_Orchestrator SHALL log an error message with the failure reason and stack trace
6. THE Schema_Indexer SHALL include timing information in completion messages showing the duration of the indexing operation

### Requirement 6: Graceful Fallback for Vector Search Failures

**User Story:** As a developer using the Text-to-SQL system, I want the system to handle vector search failures gracefully, so that I can still get results even when Qdrant is unavailable.

#### Acceptance Criteria

1. WHEN vector similarity search fails due to collection not found, THE Schema_Retriever SHALL log the error and fall back to keyword search
2. WHEN vector similarity search fails due to Qdrant connection errors, THE Schema_Retriever SHALL log the error and fall back to keyword search
3. WHEN falling back to keyword search, THE Schema_Retriever SHALL still attempt graph traversal to enhance results
4. THE Schema_Retriever SHALL include a flag in the response indicating which retrieval strategies were successfully used
5. WHEN all retrieval strategies fail, THE Schema_Retriever SHALL return an empty result set with an error message explaining the failure
6. THE Schema_Retriever SHALL not throw unhandled exceptions for retrieval failures, instead returning error information in the response structure

### Requirement 7: Embedding Model Configuration

**User Story:** As a developer deploying the Text-to-SQL system, I want embedding model parameters to be configurable, so that I can optimize for different use cases and cost constraints.

#### Acceptance Criteria

1. THE Schema_Indexer SHALL read the embedding model name from configuration with a default value of "text-embedding-3-small"
2. THE Schema_Indexer SHALL read the embedding dimension size from configuration with a default value of 1536
3. WHEN creating a Qdrant collection, THE Qdrant_Service SHALL use the configured embedding dimension size for vector configuration
4. THE Schema_Retriever SHALL use the same embedding model configuration as the Schema_Indexer for query embedding
5. WHEN the embedding model configuration changes, THE system SHALL require re-indexing of existing schemas to maintain consistency
6. THE system SHALL validate that the configured embedding dimension matches the Qdrant collection dimension before performing operations

### Requirement 8: Schema Change Detection and Re-indexing

**User Story:** As a developer working with evolving databases, I want the system to detect when schemas have changed, so that embeddings stay synchronized with the actual database structure.

#### Acceptance Criteria

1. WHEN connecting to a database, THE Agent_Orchestrator SHALL compute a schema fingerprint based on table names, column names, and relationship definitions
2. THE Schema_Indexer SHALL store the schema fingerprint as metadata in the Qdrant collection
3. WHEN a collection exists, THE Agent_Orchestrator SHALL compare the current schema fingerprint with the stored fingerprint
4. IF the schema fingerprints do not match, THEN THE Agent_Orchestrator SHALL trigger re-indexing of the schema
5. WHEN re-indexing is triggered due to schema changes, THE Schema_Indexer SHALL delete the existing collection before creating new embeddings
6. THE Agent_Orchestrator SHALL log a message when schema changes are detected indicating which tables or columns have changed
7. WHERE a force re-index option is provided, THE Agent_Orchestrator SHALL trigger re-indexing regardless of fingerprint comparison

### Requirement 9: Relationship Semantic Description Generation

**User Story:** As a developer building Text-to-SQL queries, I want foreign key relationships to have meaningful semantic descriptions, so that the system understands the business meaning of table joins.

#### Acceptance Criteria

1. WHEN indexing a foreign key relationship, THE Schema_Indexer SHALL generate a semantic description based on table and column names
2. THE Schema_Indexer SHALL use naming patterns to infer relationship semantics (e.g., "Orders.CustomerId → Customers.CustomerId" becomes "Each order belongs to a customer")
3. THE Schema_Indexer SHALL support common relationship patterns including one-to-many, many-to-one, and many-to-many through junction tables
4. WHEN a relationship involves a junction table, THE Schema_Indexer SHALL describe the many-to-many relationship between the end tables
5. THE Schema_Indexer SHALL include cardinality information in relationship descriptions when determinable from constraints
6. WHERE relationship descriptions cannot be inferred from naming, THE Schema_Indexer SHALL use a generic template: "{source_table} references {target_table} via {column_name}"

### Requirement 10: Query Embedding and Caching

**User Story:** As a developer optimizing Text-to-SQL performance, I want user queries to be efficiently embedded and cached, so that repeated similar queries execute faster.

#### Acceptance Criteria

1. WHEN a user query is received, THE Schema_Retriever SHALL generate an embedding using the Embedding_Model
2. THE Schema_Retriever SHALL cache query embeddings using the query text as the cache key
3. WHEN a cached query embedding exists and is less than 1 hour old, THE Schema_Retriever SHALL use the cached embedding instead of calling the Embedding_Model
4. THE Schema_Retriever SHALL implement cache eviction to remove embeddings older than 1 hour
5. THE Schema_Retriever SHALL limit the cache size to 1000 entries, evicting least recently used entries when the limit is exceeded
6. WHEN the Embedding_Model call fails, THE Schema_Retriever SHALL check the cache for a previous embedding of the same query and use it if available
