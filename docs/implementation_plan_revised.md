# Implementation Plan — Python Sidecar & Embedding Optimization

**Hệ thống:** TextToSqlAgent  
**Phiên bản:** 2.0 — Revised sau Senior Review  
**Ngày cập nhật:** 2026-04-21  
**Trạng thái:** Ready for implementation

---

## Tóm Tắt Thay Đổi So Với Plan Gốc

Plan gốc đề xuất dùng Python để host local embedding model (`all-MiniLM-L6-v2` / `bge-m3`). Sau review kỹ thuật, hướng này bị loại bỏ vì 3 lý do cốt lõi:

| Vấn đề | Chi tiết |
|---|---|
| **Dimension mismatch** | Qdrant collection hiện tại dùng 3072 dims (text-embedding-3-large). Cả 2 local model đều không tương thích (384 và 1024 dims) — bắt buộc phải drop và re-index toàn bộ data |
| **Quality regression** | all-MiniLM-L6-v2 chỉ đạt 56% Top-5 accuracy — không phù hợp production RAG. bge-m3 tốt hơn nhưng vẫn thấp hơn OpenAI đáng kể |
| **Kiến trúc vô nghĩa** | Nếu dùng Python để gọi tiếp OpenAI API, chỉ thêm 1 network hop thừa mà không có giá trị gì |

**Kết luận kiến trúc:**

- **Embedding** → Xử lý thẳng trong C#, switch model, không cần Python
- **Python Sidecar** → Chỉ build cho Intent Classifier và Data Visualization — đây mới là nơi Python tạo ra giá trị thực sự

---

## Kiến Trúc Tổng Thể (Revised)

```
┌─────────────────────────────────────────────────┐
│              .NET Core API (Port 5000)           │
│                                                  │
│  StreamingAgentController                        │
│       │                                          │
│       ▼                                          │
│  EnhancedAgentOrchestrator                       │
│       │                                          │
│       ├──► IIntentClassifier                     │
│       │        └── PythonIntentClassifier ──────►│──┐
│       │            (fallback: C# Regex)          │  │
│       │                                          │  │
│       ├──► IEmbeddingClient                      │  │
│       │        └── OpenAIEmbeddingClient         │  │
│       │            (text-embedding-3-small)      │  │
│       │                                          │  │
│       └──► Step 10.5: Visualization ────────────►│──┤
│                                                  │  │
└─────────────────────────────────────────────────┘  │
                                                      │
┌─────────────────────────────────────────────────┐  │
│         Python FastAPI Sidecar (Port 8100)      │◄─┘
│                                                  │
│  POST /api/classify  — ML Intent Classifier      │
│  POST /api/visualize — Chart Generation          │
│  GET  /health        — Health Check              │
└─────────────────────────────────────────────────┘
```

---

## Phase 0: Embedding Switch (C# Only — Không Cần Python)

**Ưu tiên: P0 | Effort: 30 phút | Risk: Thấp**

### Mục tiêu

Switch từ `text-embedding-3-large` sang `text-embedding-3-small` thẳng trong C#. Không thay đổi interface, không thay đổi consumer, không cần migration Qdrant nếu giữ nguyên dimension.

### So Sánh Model

| | text-embedding-3-large | text-embedding-3-small |
|---|---|---|
| Dimensions | 3072 | 1536 (hoặc giữ 3072) |
| MTEB Score | 64.6 | ~62.3 |
| Chi phí | $0.13/1M tokens | **$0.02/1M tokens** |
| Latency | ~300-500ms | ~200-350ms |
| Qdrant migration | Không | Không (nếu giữ 3072 dims) |
| Quality drop | — | ~2-3% — chấp nhận được |

### Các File Cần Thay Đổi

#### `[MODIFY]` `appsettings.json` / `.env`

Thêm config:

```
OPENAI_EMBEDDING_MODEL=text-embedding-3-small
OPENAI_EMBEDDING_DIMENSIONS=3072
```

> **Lý do giữ 3072 dims:** `text-embedding-3-small` hỗ trợ Matryoshka representation — có thể output 3072 dims mà không cần drop/re-index Qdrant. Đây là zero-migration path.

#### `[MODIFY]` `OpenAIEmbeddingClient.cs`

Đọc model name và dimension từ config thay vì hardcode. Không thay đổi interface hay signature.

#### `[MODIFY]` `EmbeddingClientFactory.cs`

Đảm bảo factory đọc model name từ config khi khởi tạo `OpenAIEmbeddingClient`.

### Verification

1. Unit test: `GenerateEmbeddingAsync` trả về vector có đúng 3072 dims
2. Integration test: `DbExplorer/search` cho kết quả semantic search vẫn chính xác
3. Benchmark: đo latency trước/sau — expect cải thiện ~20-30%
4. Chi phí: theo dõi OpenAI usage dashboard sau 1 tuần

---

## Phase 1: Python Intent ML Classifier (P1)

**Ưu tiên: P1 | Effort: 5-7 ngày | Risk: Trung bình**

### Tại Sao Python Đúng Chỗ Ở Đây

`IntentClassifier.cs` hiện tại là 1007 dòng với hàng chục mảng Regex pattern tĩnh. Mỗi lần thêm intent mới phải thêm tay 5-10 pattern. Latency biến động 5ms (regex match) đến 3 giây (LLM fallback). Accuracy ước tính ~85%.

Một model NLP nhỏ (SetFit hoặc spaCy) train trên ~500 samples có thể:
- Thay thế toàn bộ 1007 dòng bằng 1 HTTP call
- Latency ổn định 15-30ms
- Accuracy > 95% với labeled data đủ tốt
- Scale khi thêm intent mới: chỉ cần thêm training samples, không sửa code

### Kiến Trúc Module

```
User Query (vi/en)
       │
       ▼
C# PythonIntentClassifier
       │  POST /api/classify
       │  {"question": "...", "language": "vi"}
       ▼
Python FastAPI /api/classify
       │
       ├── Preprocess (normalize, tokenize)
       │
       ├── SetFit / spaCy model inference
       │
       └── Response: IntentClassificationResult JSON
              {
                "intent": "SELECT",
                "confidence": 0.97,
                "is_write_operation": false,
                "requires_confirmation": false
              }
       │
       ▼
C# maps response → IntentClassificationResult
(cùng type với C# IntentClassifier output)
```

### Các Intent Cần Cover

| Intent | Ví dụ query | Ghi chú |
|---|---|---|
| SELECT | "liệt kê khách hàng có doanh thu > 10tr" | Intent phổ biến nhất |
| AGGREGATE | "tổng doanh thu theo tháng" | SUM/COUNT/AVG |
| SCHEMA_QUERY | "bảng nào chứa thông tin đơn hàng?" | Không gen SQL |
| WRITE_INSERT | "thêm khách hàng mới" | Cần confirmation |
| WRITE_UPDATE | "cập nhật email khách hàng id 5" | Cần confirmation |
| WRITE_DELETE | "xóa đơn hàng id 10" | Cần confirmation |
| DDL | "drop table", "alter column" | Block tuyệt đối |
| AMBIGUOUS | Query không rõ ràng | Hỏi lại user |

### Files Cần Tạo/Sửa

#### Python Side

##### `[NEW]` `python-sidecar/app/routers/intent.py`

- Endpoint `POST /api/classify`
- Input: `{"question": "...", "language": "vi|en|auto"}`
- Output: `IntentClassificationResult` JSON (map 1:1 với C# type)
- Auto-detect language nếu `language = "auto"`

##### `[NEW]` `python-sidecar/models/intent_classifier/`

Thư mục chứa model artifacts:
- `model.pkl` hoặc `model.safetensors` — trained model weights
- `label_encoder.pkl` — intent label mapping
- `config.json` — model metadata (version, training date, accuracy metrics)

##### `[NEW]` `python-sidecar/training/`

- `train_intent.py` — script training/re-training model
- `evaluate.py` — script đánh giá accuracy trên test set
- `data/labeled_queries.jsonl` — labeled training data (vi + en)
- `data/test_queries.jsonl` — held-out test set (không dùng trong training)

##### `[NEW]` `python-sidecar/app/main.py`

FastAPI app:
- Load model 1 lần khi startup vào memory
- Health check `GET /health` trả về model version + accuracy metrics
- CORS config cho internal network only

##### `[NEW]` `python-sidecar/Dockerfile`

- Base: `python:3.11-slim`
- Cài: `fastapi`, `uvicorn`, `setfit` hoặc `spacy`, `scikit-learn`
- Copy model artifacts vào image
- Expose port 8100

#### C# Side

##### `[NEW]` `TextToSqlAgent.Application/Routing/PythonIntentClassifier.cs`

- Implement `IIntentClassifier` interface (đã có sẵn)
- Gọi `POST http://localhost:8100/api/classify`
- Map JSON response → `IntentClassificationResult`
- **Fallback tự động:** Nếu Python service unavailable (timeout/connection refused) → fall through tới C# `IntentClassifier` cũ
- Log warning khi fallback xảy ra (để monitor)
- Timeout: 500ms — nếu quá sẽ fallback, không block request

##### `[MODIFY]` `TextToSqlAgent.API/Program.cs`

DI registration: switch implementation dựa theo config.

```
INTENT_CLASSIFIER_PROVIDER=Python   # hoặc CSharp để dùng cũ
```

Khi `Provider=Python`: register `PythonIntentClassifier`  
Khi `Provider=CSharp`: register `IntentClassifier` cũ (giữ nguyên)

Cho phép A/B test mà không cần redeploy.

##### `[MODIFY]` `appsettings.json` / `.env`

```
INTENT_CLASSIFIER_PROVIDER=Python
PYTHON_SIDECAR_URL=http://localhost:8100
PYTHON_SIDECAR_TIMEOUT_MS=500
```

### Dataset Chuẩn Bị

Cần ít nhất 500 labeled queries trước khi training. Phân bố đề xuất:

| Intent | Số samples tối thiểu | Ghi chú |
|---|---|---|
| SELECT | 150 | Phổ biến nhất, cần nhiều nhất |
| AGGREGATE | 80 | |
| SCHEMA_QUERY | 60 | |
| WRITE_INSERT | 50 | |
| WRITE_UPDATE | 50 | |
| WRITE_DELETE | 40 | |
| DDL | 40 | Quan trọng — safety critical |
| AMBIGUOUS | 30 | Edge cases |

Nguồn data:
- Export query history từ production logs (nếu có)
- Synthetic generation từ LLM với prompt template
- Manual labeling cho edge cases tiếng Việt

### Verification

1. Train model → verify accuracy > 95% trên test set trước khi deploy
2. Integration test: gọi `/api/classify` với các edge cases nguy hiểm
   - `"xóa tất cả khách hàng"` → phải ra `WRITE_DELETE`, `confidence > 0.99`
   - `"drop table orders"` → phải ra `DDL`
   - `"doanh thu tháng này bao nhiêu"` → phải ra `AGGREGATE`
3. Fallback test: tắt Python sidecar → verify C# fallback hoạt động trong < 600ms
4. A/B test: chạy song song Python + C# trên 100 query thực tế, so sánh kết quả

---

## Phase 2: Data Visualization (P2)

**Ưu tiên: P2 | Effort: 4-5 ngày | Risk: Thấp**

### Tại Sao Python Đúng Chỗ Ở Đây

C# không có thư viện chart generation tương đương Matplotlib/Plotly/Seaborn. Generating chart image từ query result là use case hoàn toàn phù hợp cho Python — không có alternative tốt hơn trên .NET stack.

### Luồng Xử Lý

```
SQL Query Result (JSON array)
       │
       ▼
EnhancedAgentOrchestrator — Step 10.5 (sau "Format Answer")
       │  POST /api/visualize
       │  {
       │    "question": "doanh thu theo tháng",
       │    "data": [...],
       │    "chart_type": "auto"
       │  }
       ▼
Python /api/visualize
       │
       ├── Auto-detect chart type từ data shape
       │     Time series  → line chart
       │     Categorical  → bar chart
       │     Part-of-whole → pie chart
       │     Two numeric cols → scatter
       │
       ├── Generate chart với Plotly (interactive) hoặc Matplotlib (static)
       │
       └── Trả về:
              {
                "image_base64": "...",
                "chart_type": "bar",
                "title": "Doanh thu theo tháng Q1/2025",
                "should_display": true
              }
       │
       ▼
C# gắn vào AgentResponse.ChartImageBase64
       │
       ▼
Frontend render <img src="data:image/png;base64,..."/>
```

### Logic Auto-detect Chart Type

| Điều kiện data | Chart type | Lý do |
|---|---|---|
| Có cột datetime/date + 1 numeric | Line | Time series |
| 1 cột categorical + 1 numeric, ≤ 20 rows | Bar | Comparison |
| 1 cột categorical + 1 numeric, sum ≈ 100% | Pie | Composition |
| 2 cột numeric | Scatter | Correlation |
| > 5 cột hoặc > 100 rows | Table (no chart) | Quá phức tạp |
| 1 row result | No chart | Single value |

### Files Cần Tạo/Sửa

#### Python Side

##### `[NEW]` `python-sidecar/app/routers/visualize.py`

- Endpoint `POST /api/visualize`
- Auto-detect chart type từ data shape và câu hỏi gốc
- Generate chart với Plotly (prefer) hoặc Matplotlib
- Trả về Base64 PNG + metadata
- Timeout hard limit: 3 giây — nếu quá trả `should_display: false`

##### `[MODIFY]` `python-sidecar/requirements.txt`

Thêm: `plotly`, `matplotlib`, `pandas`, `kaleido` (Plotly static export)

#### C# Side

##### `[MODIFY]` `TextToSqlAgent.Core/Models/AgentResponse.cs`

Thêm 2 properties:
- `string? ChartImageBase64`
- `string? ChartType`

##### `[MODIFY]` `TextToSqlAgent.Application/Services/EnhancedAgentOrchestrator.cs`

Thêm Step 10.5 sau step format answer:
- Chỉ gọi nếu pipeline là SELECT/AGGREGATE và result có data
- Fire-and-forget với timeout 3s — nếu fail thì bỏ qua, không ảnh hưởng response chính
- Gắn kết quả vào `AgentResponse` trước khi stream về client

### Verification

1. Gọi `/api/visualize` với sample data → verify PNG Base64 decode được
2. Test auto-detect: time series data → line, sales by region → bar
3. Integration: query "doanh thu theo tháng" → response chứa `ChartImageBase64`
4. Edge case: query trả 1 row → `should_display: false`, không break response

---

## Docker Compose Changes

### `[MODIFY]` `docker-compose.yml`

Thêm service `python-sidecar`:

```yaml
python-sidecar:
  build: ./python-sidecar
  ports:
    - "8100:8100"
  environment:
    - MODEL_CACHE_DIR=/app/models
  volumes:
    - model_cache:/app/models
  healthcheck:
    test: ["CMD", "curl", "-f", "http://localhost:8100/health"]
    interval: 30s
    timeout: 10s
    retries: 3
  restart: unless-stopped

volumes:
  model_cache:
```

> **Lưu ý:** .NET API có thể start độc lập với Python sidecar nhờ fallback C# classifier. Không nên set `depends_on` cứng — để .NET API khởi động trước, Python sidecar warm up sau, fallback tự xử lý khoảng thời gian sidecar chưa ready.

---

## Thứ Tự Triển Khai & Timeline

| Phase | Task | Effort | Dependency |
|---|---|---|---|
| **Phase 0** | Switch embedding model trong C# | 0.5 ngày | Không |
| **Phase 1a** | Chuẩn bị labeled dataset (500+ queries) | 2-3 ngày | Không |
| **Phase 1b** | Train + evaluate Intent ML model | 1 ngày | Phase 1a xong |
| **Phase 1c** | Build Python `/api/classify` endpoint | 1 ngày | Phase 1b xong |
| **Phase 1d** | Build `PythonIntentClassifier.cs` + fallback | 1 ngày | Phase 1c xong |
| **Phase 1e** | Integration test + A/B test | 1 ngày | Phase 1d xong |
| **Phase 2a** | Build Python `/api/visualize` endpoint | 2 ngày | Phase 1c (sidecar đã có) |
| **Phase 2b** | Modify `AgentResponse` + Orchestrator Step 10.5 | 1 ngày | Phase 2a xong |
| **Phase 2c** | Frontend render chart Base64 | 1 ngày | Phase 2b xong |

**Tổng thời gian ước tính:** 11-13 ngày làm việc

---

## Risk Assessment

| Risk | Xác suất | Tác động | Mitigation |
|---|---|---|---|
| Intent model accuracy < 95% trên data thực | Trung bình | Cao | Fallback C# classifier; không tắt C# classifier trước khi xác nhận accuracy |
| Python sidecar crash/timeout ảnh hưởng UX | Thấp | Cao | Fallback tự động + timeout 500ms; monitor via health check |
| Visualization timeout làm chậm response | Trung bình | Thấp | Fire-and-forget với 3s timeout; chart optional, không block |
| Dataset tiếng Việt không đủ đa dạng | Cao | Trung bình | Augment bằng LLM synthetic generation; review kỹ edge cases |

---

## Không Làm Trong Scope Này

Các item sau **không nằm** trong plan này và sẽ đánh giá riêng:

- Local embedding model (đã loại bỏ sau review — xem lý do ở đầu tài liệu)
- gRPC communication thay HTTP (over-engineering cho giai đoạn này)
- Model serving với Triton/TorchServe (chỉ cần khi scale > 1000 req/s)
- Re-training pipeline tự động (manual re-train khi cần là đủ)
