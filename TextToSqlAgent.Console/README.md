# TextToSqlAgent Console - Agentic AI Edition

## Giới thiệu

Console application với đầy đủ tính năng Agentic AI:
- 🤖 Agent reasoning transparency
- 💬 Multi-turn conversations
- 🔧 Self-correction với explanation
- 📊 Real-time metrics
- 📖 Query explanation

## Quick Start

### 1. Cài đặt

```bash
cd TextToSqlAgent.Console
dotnet restore
dotnet build
```

### 2. Cấu hình

Chạy lần đầu sẽ tự động mở setup wizard:

```bash
dotnet run
```

Hoặc cấu hình thủ công:
- Set environment variable: `OPENAI_API_KEY=sk-...`
- Hoặc dùng command `/config` trong app

### 3. Chạy

```bash
dotnet run
```

## Tính năng mới

### 🤖 Agent Reasoning

Agent hiển thị từng bước suy nghĩ:

```
🤖 Agent Reasoning:
  ✓ Step 0: Validate query relevance
  📝 Step 1: Normalize with conversation context
  🗄️ Step 2: Use cached schema
  🔍 Step 3: RAG - Retrieve relevant schema
  🧠 Step 4: Analyze intent
  ⚙️ Step 5: Generate SQL with RAG context
  ✓ Step 6: Validate SQL safety
  ▶️ Step 7: Execute SQL with self-correction
  💬 Step 8: Format intelligent answer
```

### 💬 Multi-turn Conversations

Agent nhớ context của câu hỏi trước:

```
Q1: "How many customers?"
A1: "Found 150 records."

Q2: "Show me the top 5"  ← Agent hiểu "top 5 customers"
A2: "Retrieved 5 records."
```

**Commands**:
- `/new` - Bắt đầu conversation mới
- `/context` - Xem conversation history

### 🔧 Self-Correction

Agent tự động sửa lỗi SQL và giải thích:

```
🔧 Self-Correction: 2 attempt(s)
┌───┬──────────────────────────┬─────────────────────┐
│ # │ Error                    │ Fix                 │
├───┼──────────────────────────┼─────────────────────┤
│ 1 │ Invalid column 'name'    │ Changed to 'Name'   │
│ 2 │ Missing JOIN condition   │ Added FK constraint │
└───┴──────────────────────────┴─────────────────────┘
```

### 📖 Query Explanation

Agent giải thích SQL query (nếu bật):

```
📖 Query Explanation
┌─────────────────────────────────────────────────┐
│ This query counts all customers in the database │
│ by using COUNT(*) on the Customers table.       │
└─────────────────────────────────────────────────┘
```

Bật trong `appsettings.json`:
```json
{
  "Agent": {
    "ExplainQueriesBeforeExecution": true
  }
}
```

### 📊 Metrics

Mỗi query hiển thị metrics:

```
✓ Success | ⏱️ 1.23s | 🔧 0 corrections | 📊 8 steps
```

Khi exit, hiển thị session summary:

```
📊 SESSION SUMMARY
┌─────────────────────┬────────┐
│ Metric              │ Value  │
├─────────────────────┼────────┤
│ Total Queries       │ 15     │
│ Successful          │ 14     │
│ Failed              │ 1      │
│ Success Rate        │ 93%    │
│ Avg Processing Time │ 1.45s  │
│ Max Processing Time │ 3.21s  │
│ Avg Corrections     │ 0.3    │
│ Avg Steps           │ 8.2    │
└─────────────────────┴────────┘
```

## Commands

### Basic
- `help`, `?` - Hiển thị help
- `examples` - Câu hỏi mẫu
- `clear`, `cls` - Xóa màn hình
- `exit`, `quit` - Thoát

### Conversation (NEW)
- `/new` - Bắt đầu conversation mới
- `/context` - Xem conversation history

### Configuration
- `/config` - Xem/cập nhật config
- `/api-key` - Cập nhật OpenAI API key
- `/reset` - Reset configuration

### Schema & Index
- `index` - Index database schema vào vector DB
- `reindex` - Xóa và index lại
- `check index` - Kiểm tra index status
- `clear cache` - Xóa schema cache

### Database
- `show db` - Hiển thị connection hiện tại
- `switch db` - Đổi database

### Debug
- `debug` - Chẩn đoán Qdrant
- `recreate` - Tạo lại Qdrant collection

## Configuration

### appsettings.json

```json
{
  "OpenAI": {
    "Model": "gpt-4o",
    "Temperature": 0.3,
    "MaxTokens": 8192
  },
  "Agent": {
    "UseLegacyMode": false,  // false = Enhanced Agentic AI
    "MaxIterations": 12,
    "EnableReflection": true,
    "EnableSelfCorrection": true,
    "MaxSelfCorrectionAttempts": 3,
    "EnableSQLExplanation": true,  // Bật query explanation
    "EnableDetailedLogging": true
  },
  "RAG": {
    "TopK": 8,
    "MinScore": 0.75,
    "EnableHybridSearch": true
  }
}
```

## Architecture

```
Console Application
├─ EnhancedAgentOrchestrator (Agentic AI)
│  ├─ Query Validation
│  ├─ Context-aware Processing
│  ├─ Self-correction
│  └─ Query Explanation
│
├─ ConversationManager
│  ├─ Multi-turn support
│  └─ Context tracking
│
├─ UI Components
│  ├─ AgentStepRenderer (reasoning display)
│  ├─ ResponseFormatter
│  └─ ConsoleUI
│
└─ Observability
   ├─ ConsoleMetrics
   └─ Session summary
```

## Troubleshooting

### Agent không hiển thị reasoning steps
- Kiểm tra `Agent.EnableDetailedLogging = true` trong config

### Conversation không nhớ context
- Dùng `/context` để xem conversation state
- Dùng `/new` để bắt đầu conversation mới

### Metrics không chính xác
- Metrics reset mỗi session
- Chỉ track queries, không track commands

### Performance chậm
- Kiểm tra Qdrant connection (`debug` command)
- Giảm `Agent.MaxIterations` nếu cần
- Tắt `EnableSQLExplanation` để tăng tốc

## Examples

### Basic Query
```
Q: How many customers?
A: Found 150 records.
```

### Follow-up Question
```
Q: How many customers?
A: Found 150 records.

Q: Show me the top 5
💬 Conversation turn #2 (context-aware)
A: Retrieved 5 records.
```

### Self-Correction
```
Q: List all products with their category names
🔧 Self-Correction: 1 attempt(s)
  Error: Missing JOIN
  Fix: Added JOIN on CategoryId
A: Retrieved 50 records.
```

## Tips

- Dùng `/new` khi chuyển topic để tránh context confusion
- Xem agent reasoning để hiểu tại sao query failed
- Check metrics để monitor performance
- Dùng `debug` command khi có vấn đề với RAG

## Support

- Documentation: `docs/`
- Issues: GitHub Issues
- Examples: `examples` command trong app
