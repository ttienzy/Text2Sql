# Conversation-Aware Features - Frontend

## 🎯 Tổng quan

Frontend đã được cập nhật để hỗ trợ đầy đủ các tính năng conversation-aware của TextToSQL Agent v2. Người dùng giờ đây có thể:

- Duy trì context qua nhiều câu hỏi
- Xem analytics conversation real-time  
- Theo dõi token usage và cost
- Chuyển đổi giữa các conversations
- Nhận diện follow-up questions tự động

## 🆕 Components Mới

### 1. ConversationContext
**File**: `src/components/chat/ConversationContext.jsx`

Hiển thị analytics conversation trong InfoPanel:
- Message count & turn count
- Token usage & cost tracking
- Topics extraction
- Timestamps & conversation metadata
- Expandable details view

### 2. ConversationStatus  
**File**: `src/components/chat/ConversationStatus.jsx`

Status bar dưới chat input:
- Compact conversation info
- Context-aware mode indicator
- Last message timestamp
- Conversation ID (debug)

### 3. ConversationIndicator
**File**: `src/components/chat/ConversationIndicator.jsx`

Tags trong message bubbles:
- "Follow-up" cho câu hỏi liên quan
- "Context-aware" cho responses có context
- "Turn X" để đánh số lượt hội thoại
- "New" cho message đầu tiên

### 4. ConversationSwitcher
**File**: `src/components/chat/ConversationSwitcher.jsx`

Dropdown để chuyển đổi conversations:
- List tất cả conversations
- Context mode indicators
- Quick conversation creation
- Active conversation highlighting

## 🔄 Cập nhật Components Hiện tại

### ChatArea.jsx
- Sử dụng `useProcessMessageV2Mutation` cho v2 API
- Tích hợp SmartProcessingProgress
- Hiển thị ConversationStatus
- Enhanced error handling

### InfoPanel.jsx  
- Thêm ConversationContext component
- Real-time conversation analytics
- Improved layout và spacing

### ChatLayout.jsx
- Preserved existing functionality
- Better integration với conversation features

## 🛠 API Integration

### v2 API Client
**File**: `src/api/agent/v2.js`

```javascript
// Process message với conversation context
const response = await processMessageV2({
  connectionId: 'connection-id',
  question: 'Your question',
  conversationId: 'conversation-id', // optional
  includeFullHistory: true,
  maxHistoryMessages: 20
});

// Get conversation analytics
const context = await getConversationContext('conversation-id');
```

### React Query Hooks
- `useProcessMessageV2Mutation()` - Enhanced message processing
- `useConversationContextQuery()` - Real-time analytics

## 🎨 UI/UX Improvements

### Loading States
- **SmartProcessingProgress**: ReAct agent simulation
- **EnhancedProcessingProgress**: Professional progress indicators  
- **ProcessingProgress**: Basic loading states

### Visual Indicators
- Color-coded conversation tags
- Context-aware mode badges
- Follow-up question detection
- Real-time analytics updates

### Responsive Design
- Compact mode cho mobile
- Expandable details panels
- Efficient space utilization

## 📊 Analytics Features

### Real-time Metrics
- Message count tracking
- Turn-based conversation flow
- Token usage monitoring
- Cost calculation
- Topic extraction

### Performance Tracking
- Response time monitoring
- Error rate tracking
- User engagement metrics

## 🧪 Testing Guide

### 1. Khởi động Development
```bash
# Terminal 1: API
cd TextToSqlAgent.API
dotnet run

# Terminal 2: Frontend  
cd frontend
npm run dev

# Hoặc sử dụng script
./start-dev.ps1
```

### 2. Test Scenarios

#### Basic Conversation
1. Login và chọn connection
2. Tạo conversation mới
3. Gửi câu hỏi: "Show me all users"
4. Kiểm tra loading progress và response
5. Verify conversation analytics

#### Follow-up Questions
1. Tiếp tục từ conversation trên
2. Gửi: "How many users are there?"
3. Kiểm tra follow-up detection
4. Verify context preservation

#### Conversation Management
1. Tạo multiple conversations
2. Switch giữa conversations
3. Verify context isolation
4. Check analytics per conversation

### 3. Debug Tools

#### Browser DevTools
- Network tab: API calls
- Console: Error messages
- React DevTools: Component state

#### API Logs
- Conversation processing
- Context loading
- Follow-up detection

## 🔧 Configuration

### Environment Variables
```bash
# Frontend (.env)
VITE_API_BASE_URL=http://localhost:5251
VITE_ENABLE_CONVERSATION_FEATURES=true

# API (.env)
OPENAI_API_KEY=your-openai-key
JWT_SECRET=your-jwt-secret
```

### Feature Flags
```javascript
// src/constants/index.js
export const FEATURES = {
  CONVERSATION_AWARE: true,
  REAL_TIME_ANALYTICS: true,
  FOLLOW_UP_DETECTION: true,
  CONTEXT_PRESERVATION: true
};
```

## 🚀 Deployment Notes

### Production Considerations
- Enable conversation analytics caching
- Configure proper error boundaries
- Set up monitoring cho v2 API endpoints
- Optimize bundle size

### Performance Optimization
- Lazy load conversation components
- Implement virtual scrolling cho long conversations
- Cache conversation context data
- Optimize re-renders

## 📈 Future Enhancements

### Planned Features
- Conversation summarization
- Advanced topic modeling
- Conversation branching
- Export conversation history
- Collaborative conversations

### Technical Improvements
- WebSocket real-time updates
- Offline conversation support
- Advanced caching strategies
- Performance monitoring

## 🐛 Troubleshooting

### Common Issues

#### API v2 Not Working
- Check API server running on port 5251
- Verify authentication tokens
- Check network requests in DevTools

#### Context Not Loading
- Verify conversation ID in requests
- Check ConversationContext component props
- Validate API response structure

#### Follow-up Not Detected
- Check conversation history in API
- Verify message ordering
- Check follow-up detection logic

### Debug Commands
```bash
# Check API health
curl http://localhost:5251/health

# Test v2 endpoint
curl -X POST http://localhost:5251/api/v2/agent/process \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{"connectionId":"test","question":"test"}'
```

## 📞 Support

Nếu gặp vấn đề với conversation features:

1. Check `frontend/test-conversation-features.md`
2. Review API logs trong console
3. Verify component props và state
4. Check network requests trong DevTools

Frontend conversation-aware features đã sẵn sàng để test! 🎉