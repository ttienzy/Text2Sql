# 🔄 Hướng Dẫn Chuyển Đổi LLM Provider (Gemini ↔ OpenAI)

## 📋 Tổng Quan

Dự án TextToSqlAgent hỗ trợ 2 LLM providers:

- **Gemini** (Google AI)
- **OpenAI** (ChatGPT)

Bạn có thể dễ dàng chuyển đổi giữa 2 providers này bằng cách thay đổi cấu hình.

---

## ⚡ Cách Chuyển Đổi Provider

### Bước 1: Mở file `appsettings.json`

```
TextToSqlAgent.Console/appsettings.json
```

### Bước 2: Thay đổi giá trị `LLMProvider`

```json
{
  "LLMProvider": "OpenAI" // Đổi thành "Gemini" hoặc "OpenAI"
}
```

**Chỉ cần thay đổi 1 dòng này là xong!** ✅

---

## 🔑 Cấu Hình API Keys

### Option 1: Sử dụng User Secrets (Khuyến nghị - Bảo mật nhất)

#### Cho OpenAI:

```bash
dotnet user-secrets set "OpenAI:ApiKey" "sk-your-openai-api-key-here"
```

#### Cho Gemini:

```bash
dotnet user-secrets set "Gemini:ApiKey" "your-gemini-api-key-here"
```

### Option 2: Sử dụng Environment Variables

#### Windows (PowerShell):

```powershell
# OpenAI
$env:OPENAI_API_KEY = "sk-your-openai-api-key-here"

# Gemini
$env:GEMINI_API_KEY = "your-gemini-api-key-here"
```

#### Linux/Mac (Bash):

```bash
# OpenAI
export OPENAI_API_KEY="sk-your-openai-api-key-here"

# Gemini
export GEMINI_API_KEY="your-gemini-api-key-here"
```

### Option 3: Trực tiếp trong appsettings.Development.json (Không khuyến nghị cho production)

```json
{
  "OpenAI": {
    "ApiKey": "sk-your-openai-api-key-here"
  },
  "Gemini": {
    "ApiKey": "your-gemini-api-key-here"
  }
}
```

⚠️ **Lưu ý**: Không commit API keys vào Git! Thêm `appsettings.Development.json` vào `.gitignore`

---

## 🎛️ Cấu Hình Chi Tiết

### OpenAI Configuration

```json
{
  "LLMProvider": "OpenAI",
  "OpenAI": {
    "Model": "gpt-4o-mini", // Hoặc: "gpt-4o", "gpt-3.5-turbo"
    "EmbeddingModel": "text-embedding-3-small", // Hoặc: "text-embedding-3-large"
    "MaxTokens": 4096,
    "Temperature": 0.1, // 0.0-2.0 (thấp = deterministic, cao = creative)
    "OrganizationId": "" // Optional
  }
}
```

**Models OpenAI phổ biến:**

- `gpt-4o` - Mạnh nhất, đắt nhất
- `gpt-4o-mini` - Cân bằng giữa giá và hiệu suất ⭐ (Khuyến nghị)
- `gpt-3.5-turbo` - Rẻ nhất, nhanh nhất

**Embedding Models:**

- `text-embedding-3-small` - 1536 dimensions, rẻ ⭐ (Khuyến nghị)
- `text-embedding-3-large` - 3072 dimensions, chính xác hơn

### Gemini Configuration

```json
{
  "LLMProvider": "Gemini",
  "Gemini": {
    "Model": "gemini-2.5-flash", // Hoặc: "gemini-2.0-pro", "gemini-1.5-pro"
    "EmbeddingModel": "gemini-embedding-001",
    "MaxTokens": 8192,
    "Temperature": 0.1 // 0.0-1.0
  }
}
```

**Models Gemini phổ biến:**

- `gemini-2.5-flash` - Nhanh, miễn phí ⭐ (Khuyến nghị)
- `gemini-2.0-pro` - Mạnh hơn, chính xác hơn
- `gemini-1.5-pro` - Phiên bản cũ, ổn định

---

## 🔄 Vector Size cho Qdrant

**Quan trọng**: Khi đổi provider, bạn cần cập nhật `VectorSize` trong Qdrant config:

```json
{
  "Qdrant": {
    "VectorSize": 1536 // Cho OpenAI text-embedding-3-small
    // Hoặc 3072 cho text-embedding-3-large hoặc Gemini
  }
}
```

### Bảng Vector Sizes:

| Provider | Model                  | Vector Size |
| -------- | ---------------------- | ----------- |
| OpenAI   | text-embedding-3-small | 1536        |
| OpenAI   | text-embedding-3-large | 3072        |
| Gemini   | gemini-embedding-001   | 768         |
| Gemini   | text-embedding-004     | 768         |

⚠️ **Lưu ý**: Nếu đổi VectorSize, bạn cần **xóa và tạo lại Qdrant collection**!

---

## 📝 Ví Dụ Hoàn Chỉnh

### Sử dụng OpenAI (Cấu hình hiện tại - mặc định)

**appsettings.json:**

```json
{
  "LLMProvider": "OpenAI",
  "OpenAI": {
    "Model": "gpt-4o-mini",
    "EmbeddingModel": "text-embedding-3-small",
    "MaxTokens": 4096,
    "Temperature": 0.1
  },
  "Qdrant": {
    "VectorSize": 1536
  }
}
```

**Set API Key:**

```bash
dotnet user-secrets set "OpenAI:ApiKey" "sk-proj-..."
```

### Chuyển sang Gemini

**appsettings.json:**

```json
{
  "LLMProvider": "Gemini",
  "Gemini": {
    "Model": "gemini-2.5-flash",
    "EmbeddingModel": "gemini-embedding-001",
    "MaxTokens": 8192,
    "Temperature": 0.1
  },
  "Qdrant": {
    "VectorSize": 768
  }
}
```

**Set API Key:**

```bash
dotnet user-secrets set "Gemini:ApiKey" "AIzaSy..."
```

**Xóa và tạo lại Qdrant collection** (vì VectorSize khác):

```bash
# Restart Qdrant hoặc xóa collection qua API/UI
# Chạy lại schema indexing
```

---

## ✅ Kiểm Tra Cấu Hình

Khi chạy ứng dụng, bạn sẽ thấy thông báo xác nhận provider:

```
✅ Using OpenAI Provider - Model: gpt-4o-mini, Embedding: text-embedding-3-small
```

Hoặc:

```
✅ Using Gemini Provider - Model: gemini-2.5-flash, Embedding: gemini-embedding-001
```

---

## ❌ Xử Lý Lỗi

### Lỗi: "No overload for method 'ValidateConfiguration'"

✅ **Đã fix** - Code đã được cập nhật để hỗ trợ cả 2 providers

### Lỗi: "API Key not found"

```
❌ OpenAI API Key not found!

Please set it using one of these methods:
1. User Secrets: dotnet user-secrets set "OpenAI:ApiKey" "YOUR_KEY"
2. Environment Variable: OPENAI_API_KEY=YOUR_KEY
3. appsettings.Development.json (not recommended for production)
```

**Giải pháp**: Set API key theo 1 trong 3 cách trên

### Lỗi: "Vector size mismatch"

```
Collection vector size (768) doesn't match embedding size (1536)
```

**Giải pháp**:

1. Cập nhật `Qdrant:VectorSize` trong appsettings.json
2. Xóa và tạo lại Qdrant collection

---

## 🎯 Khuyến Nghị

### Cho Development (Phát triển):

- **Provider**: OpenAI (gpt-4o-mini) hoặc Gemini (gemini-2.5-flash - miễn phí)
- **API Key**: User Secrets
- **Temperature**: 0.1 (ổn định, dễ debug)

### Cho Production:

- **Provider**: OpenAI (gpt-4o-mini) - ổn định, đáng tin cậy
- **API Key**: Environment Variables hoặc Azure Key Vault
- **Temperature**: 0.1
- **MaxTokens**: Tùy nhu cầu

---

## 💡 Tips

1. **Debug nhanh**: Dùng Gemini (miễn phí) khi đang code
2. **Production**: Dùng OpenAI (ổn định hơn)
3. **Luôn giữ cả 2 API keys** trong User Secrets để dễ chuyển đổi
4. **Backup cấu hình**: Lưu cả 2 configs trong file riêng

---

## 📞 Hỗ Trợ

Nếu gặp vấn đề, kiểm tra:

1. ✅ API Key đã set đúng chưa
2. ✅ `LLMProvider` đúng tên ("Gemini" hoặc "OpenAI")
3. ✅ Model names đúng
4. ✅ VectorSize khớp với embedding model
5. ✅ Qdrant đang chạy (nếu dùng RAG)

---

**Tạo bởi**: TextToSqlAgent Team  
**Cập nhật**: 2026-02-11
