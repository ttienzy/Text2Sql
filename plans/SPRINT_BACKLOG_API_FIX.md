# Sprint Backlog: Fix API Blocking Issues

**Ngày tạo:** 2026-03-14  
**Dự án:** TextToSqlAgent  
**Mục tiêu:** Giải quyết các vấn đề API bị chặn dựa trên kết quả phân tích codebase

---

## TÓM TẮT KẾT QUẢ PHÂN TÍCH

| STT | Vấn đề                                | Trạng thái      | Ưu tiên       |
| --- | ------------------------------------- | --------------- | ------------- |
| 1   | ISqlExecutor DI registration          | ✅ ĐÃ ĐÚNG      | Không cần fix |
| 2   | Endpoint async/await                  | ✅ ĐÃ ĐÚNG      | Không cần fix |
| 3   | Timeout 5 phút                        | ✅ ĐÃ CÓ        | Không cần fix |
| 4   | Rate Limiting bị TẮT trong Production | ❌ CẦN FIX      | Cao           |
| 5   | OpenAI API Key để trống               | ⚠️ CẦN KIỂM TRA | Trung bình    |
| 6   | KHÔNG CÓ streaming endpoint           | ❌ CẦN FIX      | Rất cao       |

---

## SPRINT BACKLOG

### 🔴 Priority 1: Streaming Endpoint Implementation

#### Task 1.1: Tạo Streaming Endpoint cho Agent API

- **Mô tả:** Triển khai Server-Sent Events (SSE) endpoint để trả kết quả theo luồng thay vì đợi toàn bộ response
- **Độ ưu tiên:** P0 - Critical
- **Ước thời gian:** 2-3 giờ
- **Files cần thay đổi:**
  - `TextToSqlAgent.API/Controllers/AgentController.cs` - Thêm endpoint streaming mới
  - Hoặc tạo `ProductionStreamingAgentController.cs`
- **Acceptance Criteria:**
  - [ ] Tạo endpoint `/api/agent/stream` với SSE
  - [ ] Client có thể nhận từng phần kết quả khi có dữ liệu
  - [ ] Timeout vẫn được áp dụng (5 phút)
  - [ ] Xử lý errorgracefully khi stream bị gián đoạn
- **Ghi chú:** Đây là vấn đề CHÍNH gây ra blocking - frontend phải đợi toàn bộ response

#### Task 1.2: Cập nhật Frontend để hỗ trợ Streaming

- **Mô tả:** Sửa frontend để sử dụng streaming endpoint thay vì polling
- **Độ ưu tiên:** P0 - Critical
- **Ước thời gian:** 2 giờ
- **Files cần thay đổi:**
  - `frontend/src/hooks/useAgentQuery.js` - Thêm logic nhận stream
  - `frontend/src/api/agent/clarification.js` - Cập nhật API calls
- **Acceptance Criteria:**
  - [ ] Frontend kết nối được với SSE endpoint
  - [ ] Hiển thị progress indicator khi đang nhận stream
  - [ ] Xử lý partial updates cho UI

---

### 🟡 Priority 2: Rate Limiting Configuration

#### Task 2.1: Bật Rate Limiting trong Production

- **Mô tả:** Kích hoạt rate limiting để bảo vệ API khỏi bị spam
- **Độ ưu tiên:** P1 - High
- **Ước thời gian:** 1 giờ
- **Files cần thay đổi:**
  - `TextToSqlAgent.API/appsettings.Production.example.json`
  - `TextToSqlAgent.API/Program.cs` - Kiểm tra cấu hình rate limiting
- **Acceptance Criteria:**
  - [ ] `EnableRateLimiting: true` trong production config
  - [ ] Đặt giới hạn hợp lý (ví dụ: 100 requests/phút)
  - [ ] Cấu hình bypass cho health checks
  - [ ] Response trả về proper headers (X-RateLimit-Limit, X-RateLimit-Remaining)

---

### 🟡 Priority 3: OpenAI API Key Configuration

#### Task 3.1: Verify và Document API Key Configuration

- **Mô tả:** Đảm bảo OpenAI API key được cấu hình đúng trong production
- **Độ ưu tiên:** P2 - Medium
- **Ước thời gian:** 30 phút
- **Files cần kiểm tra:**
  - `TextToSqlAgent.API/.env.example`
  - `TextToSqlAgent.API/appsettings.json`
  - `TextToSqlAgent.API/appsettings.Production.example.json`
- **Acceptance Criteria:**
  - [ ] Kiểm tra biến môi trường OpenAIApiKey được load đúng
  - [ ] Thêm validation khi API key missing
  - [ ] Cập nhật CONFIG_README.md với hướng dẫn production
  - [ ] Log warning khi API key đang sử dụng giá trị mặc định/test

---

## TIẾN ĐỘ SPRINT

| Task | Tên                    | Ưu tiên | Trạng thái | Giờ ước tính | Thực tế |
| ---- | ---------------------- | ------- | ---------- | ------------ | ------- |
| 1.1  | Tạo Streaming Endpoint | P0      | ⏳ Pending | 2-3h         | -       |
| 1.2  | Frontend Streaming     | P0      | ⏳ Pending | 2h           | -       |
| 2.1  | Rate Limiting Config   | P1      | ⏳ Pending | 1h           | -       |
| 3.1  | API Key Config         | P2      | ⏳ Pending | 0.5h         | -       |

**Tổng ước thời gian:** 5.5-6.5 giờ

---

## GHI CHÚ

1. **Task 1.1 và 1.2** là 2 tasks quan trọng nhất - không có streaming, frontend sẽ bị block trong suốt thời gian xử lý query (có thể đến 5 phút)

2. **Task 2.1** nên được thực hiện song song với Task 1.x vì không phụ thuộc

3. **Task 3.1** có thể thực hiện sau cùng vì đây là vấn đề configuration, không phải bug

4. **Test sau khi deploy:** Cần verify streaming hoạt động đúng bằng cách test với query phức tạp (để trigger thời gian xử lý lâu)
