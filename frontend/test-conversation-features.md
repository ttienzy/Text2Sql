# Test Conversation-Aware Features

## Cách Test Frontend với Conversation-Aware Agent

### 1. Khởi động Frontend
```bash
cd frontend
npm run dev
```

### 2. Khởi động API (Terminal khác)
```bash
cd TextToSqlAgent.API
dotnet run
```

### 3. Test Scenarios

#### A. Test Basic Conversation
1. **Login** vào hệ thống
2. **Chọn connection** từ sidebar
3. **Tạo conversation mới** 
4. **Gửi câu hỏi đầu tiên**: "Show me all users in the database"
5. **Kiểm tra**:
   - Loading progress hiển thị SmartProcessingProgress
   - Response hiển thị với conversation context
   - ConversationContext panel bên phải hiển thị analytics
   - ConversationStatus ở dưới chat input

#### B. Test Follow-up Questions
1. **Tiếp tục conversation** từ bước A
2. **Gửi follow-up question**: "How many users are there in total?"
3. **Kiểm tra**:
   - Agent nhận diện đây là follow-up question
   - Response reference đến câu hỏi trước
   - ConversationContext cập nhật số turns và tokens
   - Message có ConversationIndicator "Follow-up"

#### C. Test Conversation Analytics
1. **Mở ConversationContext panel** (bên phải)
2. **Kiểm tra thông tin**:
   - Message count
   - Turn count  
   - Token usage
   - Cost tracking
   - Topics extracted
   - Timestamps

#### D. Test Conversation Switching
1. **Tạo conversation mới**
2. **Chuyển đổi giữa conversations**
3. **Kiểm tra**:
   - Context được preserve riêng biệt
   - Message history đúng cho mỗi conversation
   - Analytics riêng biệt

### 4. UI Components Mới

#### ConversationContext
- Hiển thị trong InfoPanel (bên phải)
- Analytics: messages, turns, tokens, cost
- Topics extraction
- Expandable details

#### ConversationStatus  
- Hiển thị dưới ChatInput
- Compact mode với message count
- Conversation mode indicator
- Last message time

#### ConversationIndicator
- Tags trong MessageBubble
- "Follow-up", "Context-aware", "Turn X"
- Color-coded indicators

#### ConversationSwitcher
- Dropdown trong Sidebar
- Switch between conversations
- Context mode indicators

### 5. API Endpoints Test

#### v2 Process Message
```
POST /api/v2/agent/process
{
  "connectionId": "connection-id",
  "question": "Your question",
  "conversationId": "conversation-id", // optional for new conversation
  "includeFullHistory": true,
  "maxHistoryMessages": 20
}
```

#### Conversation Context
```
GET /api/v2/agent/conversation/{conversationId}/context
```

### 6. Expected Behavior

#### First Message
- Tạo conversation ID mới
- No follow-up indicators
- Basic analytics

#### Follow-up Messages  
- Sử dụng existing conversation ID
- Follow-up detection
- Enhanced context in responses
- Updated analytics

#### Conversation Analytics
- Real-time updates
- Token/cost tracking
- Topic extraction
- Performance metrics

### 7. Debug Tips

#### Check Browser Console
- API calls to v2 endpoints
- Response data structure
- Error messages

#### Check Network Tab
- v2 API requests
- Response payloads
- Conversation context calls

#### Check API Logs
- Conversation-aware processing
- Context loading
- Follow-up detection

### 8. Common Issues

#### API Not Using v2
- Verify ChatArea uses `useProcessMessageV2Mutation`
- Check API base URL
- Verify authentication

#### Context Not Loading
- Check conversation ID in requests
- Verify ConversationContext component
- Check API response structure

#### Follow-up Not Detected
- Verify conversation history in API
- Check follow-up detection logic
- Verify message ordering

### 9. Success Criteria

✅ **Basic Functionality**
- Messages send and receive
- Loading states work
- Error handling works

✅ **Conversation Features**
- Conversation ID generated/maintained
- Follow-up questions detected
- Context preserved across turns

✅ **UI Enhancements**
- ConversationContext shows analytics
- ConversationStatus displays correctly
- Loading progress enhanced

✅ **API Integration**
- v2 endpoints working
- Conversation context API working
- Proper error handling

### 10. Performance Notes

- ConversationContext refreshes every 30s
- Analytics calculated server-side
- Efficient context loading
- Proper caching strategies