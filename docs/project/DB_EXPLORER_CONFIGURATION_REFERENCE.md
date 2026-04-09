# DB Explorer Configuration Reference
## Enterprise-Ready Configuration Guide

**Date:** 2026-04-08  
**Purpose:** Complete reference for all configurable aspects of AI DB Explorer

---

## 📋 Table of Contents

1. [appsettings.json Configuration](#appsettingsjson-configuration)
2. [Prompt Templates](#prompt-templates)
3. [Health Check Rules](#health-check-rules)
4. [Connection System Context](#connection-system-context)
5. [Security Settings](#security-settings)

---

## 1. appsettings.json Configuration

### Complete Configuration Schema

```json
{
  "DbExplorer": {
    "HealthCheck": {
      "MaxColumnsPerTable": 50,
      "ImplicitFkConfidenceThreshold": 0.85,
      "MinRowsForStatistics": 1000000,
      "IgnoreTablesRegex": "^(dbo|sys|__EFMigrationsHistory|sysdiagrams)",
      "PasswordColumnPatterns": ["password", "pwd", "pass", "secret"],
      "AuditColumnNames": ["CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy"]
    },
    "NamingConvention": {
      "PreferredStyle": "PascalCase",
      "AllowedStyles": ["PascalCase", "snake_case", "camelCase"],
      "StrictMode": false,
      "CustomPatterns": {
        "TablePrefix": "",
        "ColumnPrefix": "",
        "ForeignKeyPattern": "^{ParentTable}Id$"
      }
    },
    "AI": {
      "LazyLoadingEnabled": true,
      "BatchSize": {
        "Tables": 10,
        "Columns": 20
      },
      "CacheTTL": {
        "SchemaAnalysis": "24:00:00",
        "ColumnInterpretation": "7.00:00:00",
        "SemanticTags": "30.00:00:00"
      },
      "Prompts": {
        "BasePath": "Prompts/DbExplorer",
        "Temperature": 0.7,
        "MaxTokens": 2000
      }
    },
    "Security": {
      "AllowSampleDataQuery": false,
      "MaxSampleRows": 5,
      "RequireExplicitConsent": true,
      "AuditDataAccess": true
    },
    "Performance": {
      "MaxTablesForInitialLoad": 500,
      "TimeoutSeconds": {
        "SchemaScanning": 60,
        "AIAnalysis": 30,
        "HealthCheck": 10
      },
      "EnableParallelProcessing": true
    },
    "ImplicitFkDetection": {
      "Enabled": true,
      "ConfidenceThreshold": 0.75,
      "NamingPatterns": [
        "^{ParentTable}Id$",
        "^{ParentTable}_ID$",
        "^Ma{ParentTable}$",
        "^{ParentTable}Code$",
        "^ID{ParentTable}$"
      ],
      "RequireLLMConfirmation": true,
      "AllowDataValidation": false
    },
    "SemanticSearch": {
      "Enabled": true,
      "MinRelevanceScore": 0.6,
      "MaxResults": 20,
      "GenerateSemanticTags": true,
      "SupportedLanguages": ["vi", "en"]
    }
  }
}
```

### Configuration Sections Explained

#### HealthCheck
- `MaxColumnsPerTable`: Threshold for "too many columns" warning
- `ImplicitFkConfidenceThreshold`: Minimum confidence for suggesting implicit FK
- `MinRowsForStatistics`: Skip detailed stats for tables larger than this
- `IgnoreTablesRegex`: System tables to exclude from analysis
- `PasswordColumnPatterns`: Patterns to detect password columns
- `AuditColumnNames`: Expected audit trail column names

#### NamingConvention
- `PreferredStyle`: Recommended naming style for the database
- `AllowedStyles`: Acceptable naming styles (won't trigger warnings)
- `StrictMode`: If true, enforce preferred style strictly
- `CustomPatterns`: Organization-specific naming patterns

#### AI
- `LazyLoadingEnabled`: Enable on-demand AI analysis
- `BatchSize`: Number of items to process in one LLM call
- `CacheTTL`: Time-to-live for cached AI results
- `Prompts`: Prompt template configuration

#### Security
- `AllowSampleDataQuery`: Allow querying actual data (privacy risk)
- `MaxSampleRows`: Limit sample data rows
- `RequireExplicitConsent`: Show consent dialog before data access
- `AuditDataAccess`: Log all data access operations

#### Performance
- `MaxTablesForInitialLoad`: Limit for initial schema scan
- `TimeoutSeconds`: Timeouts for various operations
- `EnableParallelProcessing`: Use parallel processing for large databases

#### ImplicitFkDetection
- `Enabled`: Enable implicit FK detection
- `ConfidenceThreshold`: Minimum confidence to suggest FK
- `NamingPatterns`: Regex patterns for FK column names
- `RequireLLMConfirmation`: Use LLM to confirm ambiguous cases
- `AllowDataValidation`: Allow optional data-based validation

#### SemanticSearch
- `Enabled`: Enable semantic search feature
- `MinRelevanceScore`: Minimum relevance score to show results
- `MaxResults`: Maximum search results to return
- `GenerateSemanticTags`: Auto-generate semantic tags with AI
- `SupportedLanguages`: Languages for semantic search

---

## 2. Prompt Templates

### Directory Structure

```
Prompts/
└── DbExplorer/
    ├── schema-summary.skprompt.txt
    ├── column-interpretation.skprompt.txt
    ├── implicit-fk-detection.skprompt.txt
    ├── semantic-tags.skprompt.txt
    └── config.json
```

### Template: schema-summary.skprompt.txt

```
{{$systemContext}}

Bạn là chuyên gia phân tích database {{$domain}}.

Phân tích database này và tạo executive summary:

TABLES ({{$tableCount}} total):
{{$tableNames}}

RELATIONSHIPS ({{$relationshipCount}} total):
{{$relationships}}

Trả về JSON với format chính xác:
{
  "domain": "<domain classification>",
  "summary": "<1-2 câu mô tả tổng quan>",
  "keyTables": ["<bảng quan trọng nhất>"],
  "modules": [
    {
      "name": "<tên module>",
      "description": "<mô tả ngắn>",
      "tables": ["<danh sách bảng>"]
    }
  ],
  "technicalDebt": ["<vấn đề tiềm ẩn>"],
  "confidence": 0.85
}

Chỉ trả về JSON, không có markdown hay giải thích thêm.
```

### Template: column-interpretation.skprompt.txt

```
{{$systemContext}}

Bạn là chuyên gia database {{$domain}}.
{{$namingConventionNotes}}

Giải thích ý nghĩa các tên cột sau:

Bảng: {{$tableName}}
Mô tả bảng: {{$tableDescription}}

Columns:
{{$columns}}

Trả về JSON với format:
{
  "columnName": {
    "meaning": "Ý nghĩa tiếng Việt",
    "english": "English translation",
    "description": "Mô tả chi tiết về mục đích của cột",
    "confidence": 0.95
  }
}

Lưu ý:
- Với tên viết tắt tiếng Việt (MaKH, TenKH), hãy giải thích đầy đủ
- Với tên tiếng Anh, hãy dịch sang tiếng Việt
- Confidence score: 0.9-1.0 (chắc chắn), 0.7-0.9 (khá chắc), <0.7 (không chắc)

Chỉ trả về JSON, không có markdown.
```

### Template: implicit-fk-detection.skprompt.txt

```
{{$systemContext}}

Bạn là chuyên gia database {{$domain}}.

Phân tích xem cột sau có phải là Foreign Key ẩn không:

Bảng con: {{$childTable}}
Cột: {{$childColumn}} ({{$childDataType}})

Bảng cha tiềm năng: {{$parentTable}}
Cột PK: {{$parentColumn}} ({{$parentDataType}})

Metadata:
- Child table rows: {{$childRows}}
- Parent table rows: {{$parentRows}}
- Data type match: {{$dataTypeMatch}}
- Naming pattern match: {{$namingMatch}}

Trả về JSON:
{
  "isImplicitFk": true/false,
  "confidence": 0.85,
  "reason": "Giải thích tại sao đây là/không là FK",
  "recommendation": "Nên thêm constraint FK" hoặc "Không nên thêm FK"
}

Chỉ trả về JSON.
```

### Template: semantic-tags.skprompt.txt

```
{{$systemContext}}

Bạn là chuyên gia database {{$domain}}.

Tạo semantic tags (từ đồng nghĩa và liên quan) cho bảng sau:

Table: {{$tableName}}
Description: {{$tableDescription}}
Columns: {{$columnNames}}

Trả về JSON:
{
  "tags": ["tag1", "tag2", "tag3"],
  "vietnamese": ["từ tiếng Việt liên quan"],
  "english": ["English related terms"],
  "related_concepts": ["khái niệm nghiệp vụ liên quan"],
  "synonyms": ["từ đồng nghĩa"]
}

Ví dụ:
- Bảng "KH_DM" → tags: ["khách hàng", "customer", "user", "người mua", "CRM", "demographic", "client"]
- Bảng "Orders" → tags: ["đơn hàng", "order", "purchase", "transaction", "sales", "invoice"]

Tạo ít nhất 10 tags đa dạng để tìm kiếm semantic hiệu quả.
Chỉ trả về JSON.
```

### config.json

```json
{
  "schema-summary": {
    "temperature": 0.7,
    "max_tokens": 2000,
    "top_p": 0.9
  },
  "column-interpretation": {
    "temperature": 0.5,
    "max_tokens": 1500,
    "top_p": 0.8
  },
  "implicit-fk-detection": {
    "temperature": 0.3,
    "max_tokens": 500,
    "top_p": 0.7
  },
  "semantic-tags": {
    "temperature": 0.8,
    "max_tokens": 1000,
    "top_p": 0.9
  }
}
```

---

## 3. Health Check Rules

### Directory Structure

```
HealthCheckRules/
├── critical-rules.json
├── warning-rules.json
└── info-rules.json
```

### critical-rules.json

```json
{
  "rules": [
    {
      "id": "missing-pk",
      "name": "Missing Primary Key",
      "severity": "critical",
      "type": "metadata",
      "check": {
        "condition": "table.PrimaryKeys.Count == 0"
      },
      "message": "Table '{tableName}' has no primary key",
      "recommendation": "Add a primary key to ensure data integrity and enable efficient indexing",
      "sqlFix": "ALTER TABLE [{schema}].[{tableName}] ADD CONSTRAINT PK_{tableName} PRIMARY KEY ([Id])",
      "documentation": "https://docs.microsoft.com/sql/relational-databases/tables/primary-and-foreign-key-constraints"
    },
    {
      "id": "password-not-encrypted",
      "name": "Password Column Not Encrypted",
      "severity": "critical",
      "type": "metadata",
      "check": {
        "condition": "column.Name matches passwordPattern AND column.DataType == 'varchar'"
      },
      "message": "Column '{columnName}' in table '{tableName}' appears to store passwords without encryption",
      "recommendation": "Use hashed passwords (bcrypt, PBKDF2) or encrypted columns. Never store plain text passwords.",
      "sqlFix": "-- Migrate to hashed passwords\n-- 1. Add new column: ALTER TABLE [{tableName}] ADD PasswordHash VARBINARY(MAX)\n-- 2. Migrate data with hashing\n-- 3. Drop old column",
      "documentation": "https://docs.microsoft.com/sql/relational-databases/security/encryption/always-encrypted-database-engine"
    },
    {
      "id": "missing-fk-index",
      "name": "Foreign Key Without Index",
      "severity": "critical",
      "type": "metadata",
      "check": {
        "condition": "column.IsForeignKey AND !table.Indexes.Any(i => i.Columns.Contains(column.Name))"
      },
      "message": "Foreign key column '{columnName}' in table '{tableName}' has no index",
      "recommendation": "Create an index on this FK column to improve JOIN performance",
      "sqlFix": "CREATE INDEX IX_{tableName}_{columnName} ON [{schema}].[{tableName}]([{columnName}])",
      "estimatedImpact": "40-60% faster JOIN queries"
    }
  ]
}
```

### warning-rules.json

```json
{
  "rules": [
    {
      "id": "too-many-columns",
      "name": "Too Many Columns",
      "severity": "warning",
      "type": "metadata",
      "check": {
        "condition": "table.ColumnCount > config.MaxColumnsPerTable"
      },
      "message": "Table '{tableName}' has {columnCount} columns (threshold: {threshold})",
      "recommendation": "Consider normalizing this table into multiple related tables",
      "documentation": "https://docs.microsoft.com/sql/relational-databases/tables/database-normalization-basics"
    },
    {
      "id": "nullable-fk",
      "name": "Nullable Foreign Key",
      "severity": "warning",
      "type": "metadata",
      "check": {
        "condition": "column.IsForeignKey AND column.IsNullable"
      },
      "message": "Foreign key column '{columnName}' in table '{tableName}' is nullable",
      "recommendation": "Consider if this FK should be required (NOT NULL) for data integrity",
      "sqlFix": "-- If FK should be required:\nALTER TABLE [{tableName}] ALTER COLUMN [{columnName}] {dataType} NOT NULL"
    },
    {
      "id": "missing-audit-columns",
      "name": "Missing Audit Trail",
      "severity": "warning",
      "type": "metadata",
      "check": {
        "condition": "!table.Columns.Any(c => auditColumns.Contains(c.Name))"
      },
      "message": "Table '{tableName}' has no audit trail columns (CreatedAt, UpdatedAt, etc.)",
      "recommendation": "Add audit columns to track when records are created and modified",
      "sqlFix": "ALTER TABLE [{tableName}] ADD CreatedAt DATETIME2 DEFAULT GETUTCDATE(), UpdatedAt DATETIME2, CreatedBy NVARCHAR(100), UpdatedBy NVARCHAR(100)"
    }
  ]
}
```

### info-rules.json

```json
{
  "rules": [
    {
      "id": "orphan-table",
      "name": "Orphan Table",
      "severity": "info",
      "type": "metadata",
      "check": {
        "condition": "!schema.Relationships.Any(r => r.FromTable == table.Name || r.ToTable == table.Name)"
      },
      "message": "Table '{tableName}' has no relationships with other tables",
      "recommendation": "Verify if this table is still needed or should be connected to other tables"
    },
    {
      "id": "inconsistent-naming",
      "name": "Inconsistent Naming Convention",
      "severity": "info",
      "type": "metadata",
      "check": {
        "condition": "table.NamingStyle != config.PreferredStyle"
      },
      "message": "Table '{tableName}' uses {actualStyle} but database prefers {preferredStyle}",
      "recommendation": "Consider renaming to follow consistent naming convention: {suggestedName}"
    },
    {
      "id": "no-length-constraint",
      "name": "Text Column Without Length",
      "severity": "info",
      "type": "metadata",
      "check": {
        "condition": "column.DataType == 'varchar' AND column.MaxLength == -1"
      },
      "message": "Column '{columnName}' in table '{tableName}' is VARCHAR(MAX) without length constraint",
      "recommendation": "Specify a reasonable max length to prevent excessive storage usage",
      "sqlFix": "ALTER TABLE [{tableName}] ALTER COLUMN [{columnName}] VARCHAR(500)"
    }
  ]
}
```

---

## 4. Connection System Context

### Database Schema

```sql
ALTER TABLE Connections ADD
    SystemDomain NVARCHAR(100) NULL,
    NamingConventionNotes NVARCHAR(500) NULL,
    BusinessContext NVARCHAR(MAX) NULL;
```

### UI Form Fields

```typescript
interface ConnectionSystemContext {
  systemDomain: string; // E-commerce, ERP, CRM, Healthcare, etc.
  namingConventionNotes: string; // "Tên cột viết tắt tiếng Việt, prefix Ma = Mã"
  businessContext: string; // "Hệ thống quản lý bán hàng cho công ty sản xuất thép"
}
```

### Example Values

```json
{
  "systemDomain": "E-commerce",
  "namingConventionNotes": "Tên bảng dùng PascalCase. Tên cột viết tắt tiếng Việt: Ma = Mã, Ten = Tên, DM = Danh mục. Foreign key pattern: Ma{Table}",
  "businessContext": "Hệ thống ERP cho công ty sản xuất thép. Quản lý đơn hàng, kho, sản xuất, và kế toán. Database được migrate từ hệ thống cũ nên có một số bảng legacy."
}
```

### How It's Used

```csharp
// Inject into LLM prompts
var systemContext = $@"
SYSTEM CONTEXT:
- Domain: {connection.SystemDomain}
- Naming Convention: {connection.NamingConventionNotes}
- Business Context: {connection.BusinessContext}
";

// Use in Semantic Kernel
var context = kernel.CreateNewContext();
context["systemContext"] = systemContext;
context["domain"] = connection.SystemDomain;
context["namingConventionNotes"] = connection.NamingConventionNotes;
```

---

## 5. Security Settings

### Data Access Control

```json
{
  "Security": {
    "AllowSampleDataQuery": false,
    "MaxSampleRows": 5,
    "RequireExplicitConsent": true,
    "AuditDataAccess": true,
    "SensitiveColumnPatterns": [
      "password",
      "ssn",
      "credit_card",
      "email",
      "phone"
    ],
    "MaskSensitiveData": true
  }
}
```

### Audit Log Schema

```sql
CREATE TABLE DbExplorerAuditLog (
    Id BIGINT IDENTITY PRIMARY KEY,
    UserId NVARCHAR(450) NOT NULL,
    ConnectionId NVARCHAR(450) NOT NULL,
    Action NVARCHAR(100) NOT NULL, -- 'SampleDataQuery', 'DataValidation'
    TableName NVARCHAR(200),
    RowsAccessed INT,
    Timestamp DATETIME2 DEFAULT GETUTCDATE(),
    IpAddress NVARCHAR(50),
    UserAgent NVARCHAR(500)
);
```

### Consent Dialog (Frontend)

```jsx
<Modal title="Data Access Consent" visible={showConsent}>
  <Alert type="warning" message="Privacy Notice" />
  <p>
    This operation will query sample data from your database.
    Only {maxSampleRows} rows will be accessed for analysis.
  </p>
  <Checkbox onChange={setConsent}>
    I understand and consent to sample data access
  </Checkbox>
  <Button onClick={handleProceed} disabled={!consent}>
    Proceed
  </Button>
</Modal>
```

---

## 📝 Configuration Best Practices

### 1. Start Conservative
- Begin with `AllowSampleDataQuery: false`
- Use high confidence thresholds (0.85+)
- Enable strict security settings

### 2. Tune Based on Feedback
- Adjust thresholds based on false positives
- Refine naming patterns for your organization
- Update prompts based on accuracy

### 3. Document Custom Rules
- Add comments to JSON rule files
- Version control all configuration
- Document why thresholds were chosen

### 4. Test Configuration Changes
- Test with sample databases first
- Monitor LLM costs after changes
- Validate accuracy with known schemas

---

**Last Updated:** 2026-04-08  
**Maintained by:** AI DB Explorer Team
