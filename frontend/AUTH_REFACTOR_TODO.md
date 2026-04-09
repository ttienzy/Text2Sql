# Auth System Refactor TODO

## 🔴 CRITICAL ISSUES - Fix Immediately

### 1. DTO Property Name Inconsistency
**Problem**: Mixed usage of `refreshToken` (camelCase) and `RefreshToken` (PascalCase)

**Backend Configuration**:
- `Program.cs` line 452: `PropertyNamingPolicy = CamelCase`
- Backend DTO expects: `RefreshToken` (PascalCase) but auto-converts from camelCase
- **Conclusion**: Frontend should send `refreshToken` (camelCase) consistently

**Files to Fix**:
- ✅ `frontend/src/api/axios.js` line 117: Already using `refreshToken` (camelCase) ✅
- ❌ `frontend/src/store/authStore.js` line 213: Using `RefreshToken` (PascalCase) ❌
- ✅ `frontend/src/store/authStore.js` line 239: Already using `refreshToken` (camelCase) ✅
- ✅ `frontend/src/hooks/useStreamingQuery.js` line 90: Already using `refreshToken` (camelCase) ✅

**Action**:
```javascript
// authStore.js line 213 - startSilentRefresh()
// BEFORE:
const response = await axiosInstance.post(API_ENDPOINTS.AUTH.REFRESH, { RefreshToken: refreshToken });

// AFTER:
const response = await axiosInstance.post(API_ENDPOINTS.AUTH.REFRESH, { refreshToken });
```

---

### 2. Infinite Retry Loop - Token Not Available After Refresh
**Problem**: After successful refresh, `getAuthStore()?.accessToken` returns `null` causing retry to fail

**Root Cause Analysis**:
```
1. Request → 401
2. Interceptor calls refresh → SUCCESS ✅
3. setToken(accessToken, newRefreshToken) → Zustand updates
4. Retry original request
   └─ Request interceptor: const token = getAuthStore?.()?.accessToken
   └─ ⚠️ Returns null (timing issue or stale closure)
   └─ No Authorization header
5. Request → 401 again
6. originalRequest._retry = true (already set)
7. ⚠️ Should skip but interceptor still processes → LOOP
```

**Potential Causes**:
- Zustand state update not synchronous
- `getAuthStore` closure capturing old state
- Race condition between `setToken()` and retry

**Action**:
- Add explicit wait/verification after `setToken()`
- Add retry limit (max 1 retry per request)
- Add debug logging to track token state

---

### 3. Silent Refresh Timer Conflicts with Axios Interceptor
**Problem**: Two mechanisms trying to refresh simultaneously

**Current State**:
- ✅ `authStore.js` - Silent refresh timer DISABLED (commented out)
- ✅ `axios.js` - Interceptor handles refresh on 401
- ❌ `authStore.js` line 199-228: `startSilentRefresh()` still exists but unused

**Action**:
- Remove `startSilentRefresh()` method entirely (dead code)
- Remove `refreshTimer` from state
- Document: "Token refresh is handled by axios interceptor on-demand"

---

## 🟡 MEDIUM PRIORITY - Improve Reliability

### 4. Race Condition on Page Load
**Problem**: Multiple components fetch data before `initializeAuth()` completes

**Current Fix**:
- ✅ `App.jsx`: Added `authReady` state and loading spinner
- ✅ `MainLayout.jsx`: Wait for `isAuthenticated && accessToken` before fetching

**Remaining Issues**:
- Other components may still call APIs during initialization
- No global "auth initializing" state

**Action**:
- Add `isInitializing` flag to authStore
- Create `<AuthGuard>` wrapper component that shows loading during init
- Audit all API calls to ensure they check auth state first

---

### 5. Error Handling Inconsistency
**Problem**: Different error handling in different refresh locations

**Files**:
- `axios.js` line 130: Catches error, dispatches logout event
- `authStore.js` line 246: Catches error, clears session silently
- `useStreamingQuery.js` line 91: Catches error, throws to caller

**Action**:
- Standardize error handling strategy
- Create `handleRefreshError()` utility function
- Decide: Should refresh failure always logout or allow retry?

---

## 🟢 LOW PRIORITY - Code Quality

### 6. Remove Dead Code
**Files**:
- `authStore.js` line 199-228: `startSilentRefresh()` - unused
- `authStore.js` line 36: `refreshTimer` state - unused

**Action**:
- Delete unused code
- Update comments to reflect current architecture

---

### 7. Improve Type Safety
**Problem**: No TypeScript, easy to make mistakes with property names

**Action** (Future):
- Consider migrating to TypeScript
- Add JSDoc type annotations for now
- Create constants for DTO property names

---

### 8. Centralize Token Refresh Logic
**Problem**: Refresh logic duplicated in 3 places:
- `axios.js` interceptor
- `authStore.js` initializeAuth
- `useStreamingQuery.js` manual refresh

**Action**:
- Create `tokenService.js` with single `refreshToken()` function
- All locations call this service
- Easier to maintain and test

---

## 📋 IMPLEMENTATION PLAN

### Phase 1: Fix Critical Issues (NOW)
1. ✅ Fix DTO inconsistency in `authStore.js` line 213
2. ✅ Add retry limit in axios interceptor
3. ✅ Add debug logging for token state
4. ✅ Test: Login → Reload → Should not logout

### Phase 2: Improve Reliability (NEXT)
1. Remove `startSilentRefresh()` dead code
2. Add `isInitializing` flag to authStore
3. Audit all API calls for auth checks
4. Standardize error handling

### Phase 3: Code Quality (LATER)
1. Create `tokenService.js`
2. Add JSDoc annotations
3. Write unit tests for refresh logic
4. Document architecture decisions

---

## 🔍 DEBUGGING CHECKLIST

When refresh token fails, check:
- [ ] Backend logs: Is token in database? Is it expired? Is it revoked?
- [ ] Frontend console: What's the exact error message?
- [ ] Network tab: What's the request/response payload?
- [ ] authStore state: Is `accessToken` null after refresh?
- [ ] localStorage: Is `tts_refresh_token` present and valid?
- [ ] Timing: Is there a race condition between requests?

---

## 🎯 SUCCESS CRITERIA

After refactor, the system should:
1. ✅ Login → Access token in memory, refresh token in localStorage
2. ✅ Reload page → Silent refresh succeeds, no logout
3. ✅ Token expires → Auto-refresh on next API call
4. ✅ Refresh fails → Logout once, no infinite loop
5. ✅ Multiple tabs → Each tab manages its own tokens independently
6. ✅ Logout → All tokens cleared, redirect to login

---

## 📚 ARCHITECTURE NOTES

### Current Token Strategy (Memory-Only Access Token)
**Pros**:
- XSS protection: Access token not in localStorage
- Refresh token rotation: Old token revoked after use

**Cons**:
- Page reload requires refresh (adds latency)
- Complex state management
- Multiple refresh mechanisms can conflict

### Alternative: Both Tokens in localStorage
**Pros**:
- Simpler implementation
- No refresh on page load
- Faster initial load

**Cons**:
- XSS vulnerability: Attacker can steal both tokens
- Less secure for sensitive applications

**Decision**: Keep current strategy (memory-only access token) for security
**Requirement**: Fix implementation bugs, not change architecture

---

## 🔧 QUICK FIXES TO APPLY NOW

```javascript
// 1. authStore.js line 213 - Fix DTO inconsistency
const response = await axiosInstance.post(API_ENDPOINTS.AUTH.REFRESH, { refreshToken });

// 2. axios.js - Add retry limit
if (error.response?.status === HTTP_STATUS.UNAUTHORIZED && !originalRequest._retry && !originalRequest._refreshRetryCount) {
  originalRequest._retry = true;
  originalRequest._refreshRetryCount = (originalRequest._refreshRetryCount || 0) + 1;
  
  if (originalRequest._refreshRetryCount > 1) {
    console.error('❌ Max refresh retry limit reached');
    window.dispatchEvent(new CustomEvent('auth:logout', { detail: { reason: 'max_retry' } }));
    return Promise.reject(standardizedError);
  }
  // ... rest of refresh logic
}

// 3. axios.js - Add debug logging after setToken
authStore.setToken(accessToken, newRefreshToken);
console.log('🔍 Token set, verifying:', {
  hasToken: !!authStore.accessToken,
  tokenPreview: authStore.accessToken?.substring(0, 20)
});

// 4. Remove dead code from authStore.js
// Delete lines 199-228 (startSilentRefresh method)
// Delete line 36 (refreshTimer state)
```

---

**Last Updated**: 2026-04-08
**Status**: Ready for implementation
**Priority**: CRITICAL - Fix before production deployment
