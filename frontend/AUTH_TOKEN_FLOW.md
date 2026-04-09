# 🔐 Authentication Token Flow - Memory-Only Strategy

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         FRONTEND                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌──────────────┐         ┌──────────────┐                    │
│  │  authStore   │◄────────│   axios.js   │                    │
│  │  (Zustand)   │         │ interceptors │                    │
│  └──────────────┘         └──────────────┘                    │
│        │                         │                             │
│        │ accessToken             │ reads accessToken           │
│        │ (MEMORY-ONLY)           │ from authStore              │
│        │                         │                             │
│        │ refreshToken            │                             │
│        │ (localStorage)          │                             │
│        └─────────────────────────┘                             │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                            │
                            │ HTTPS
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                         BACKEND                                 │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌──────────────────┐         ┌──────────────────┐            │
│  │ AuthController   │────────►│ AuthService      │            │
│  │ /api/auth/*      │         │ JWT generation   │            │
│  └──────────────────┘         └──────────────────┘            │
│                                        │                        │
│                                        ▼                        │
│                              ┌──────────────────┐              │
│                              │   Database       │              │
│                              │ RefreshTokens    │              │
│                              │ - Token (string) │              │
│                              │ - ExpiresAt      │              │
│                              │ - IsRevoked      │              │
│                              │ - ReplacedByTokenId             │
│                              └──────────────────┘              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## Token Lifecycle

### 1️⃣ Login
```
User → Login Form → POST /api/auth/login
                    ↓
            Backend validates credentials
                    ↓
            Generate accessToken (JWT, 180 min)
            Generate refreshToken (random, 7 days)
            Save refreshToken to database
                    ↓
            Return {accessToken, refreshToken}
                    ↓
authStore.setToken(accessToken, refreshToken)
    ├─ accessToken → Zustand state (MEMORY)
    └─ refreshToken → localStorage
                    ↓
Start silent refresh timer (179 min)
```

### 2️⃣ API Request
```
Component → API call → axios interceptor
                       ↓
            Read accessToken from authStore.getState()
                       ↓
            Add header: Authorization: Bearer <token>
                       ↓
            Send request to backend
                       ↓
            Backend validates JWT
                       ↓
            Return response
```

### 3️⃣ Token Refresh (Silent Timer)
```
Timer (179 min) → authStore.startSilentRefresh()
                  ↓
        Read refreshToken from localStorage
                  ↓
        POST /api/auth/refresh {refreshToken}
                  ↓
        Backend validates refreshToken in database
                  ↓
        Generate NEW accessToken + refreshToken
        Revoke OLD refreshToken (set IsRevoked=true)
        Save NEW refreshToken to database
                  ↓
        Return {accessToken, refreshToken}
                  ↓
authStore.setToken(newAccessToken, newRefreshToken)
    ├─ accessToken → Zustand state (MEMORY)
    └─ refreshToken → localStorage
```

### 4️⃣ Token Refresh (401 Interceptor)
```
API Request → 401 Unauthorized
              ↓
    axios 401 interceptor catches
              ↓
    Read refreshToken from localStorage
              ↓
    POST /api/auth/refresh {refreshToken}
              ↓
    Backend validates & generates new tokens
              ↓
    authStore.setToken(newAccessToken, newRefreshToken)
              ↓
    Retry original request with new accessToken
```

### 5️⃣ Page Reload
```
User reloads page → accessToken LOST (memory-only)
                    ↓
        App.jsx useEffect → initializeAuth()
                    ↓
        Read refreshToken from localStorage
                    ↓
        POST /api/auth/refresh {refreshToken}
                    ↓
        Backend validates & generates new tokens
                    ↓
authStore.setToken(newAccessToken, newRefreshToken)
    ├─ accessToken → Zustand state (MEMORY)
    └─ refreshToken → localStorage
                    ↓
        Start silent refresh timer
                    ↓
        Session restored ✅
```

### 6️⃣ Logout
```
User clicks logout → authStore.logout()
                     ↓
        POST /api/auth/logout {refreshToken}
                     ↓
        Backend revokes refreshToken in database
                     ↓
        Clear refreshTimer
        Remove refreshToken from localStorage
        Clear Zustand state (accessToken, refreshToken)
                     ↓
        Redirect to /login
```

## Security Analysis

### ✅ XSS Protection
- **accessToken**: MEMORY-ONLY → Cannot be stolen via `document.cookie` or localStorage XSS
- **refreshToken**: localStorage → Can be stolen, but has limited scope (only for refresh)
- **Impact**: Even if XSS steals refreshToken, attacker cannot access current session (no accessToken)

### ✅ Token Rotation
- Backend revokes old refreshToken after generating new one
- `ReplacedByTokenId` tracks token chain
- Prevents replay attacks with old refresh tokens

### ✅ Automatic Cleanup
- `migrateStorage()` removes deprecated `tts_access_token` on app init
- Ensures no legacy insecure tokens remain

### ✅ Concurrent Refresh Protection
- `refreshTokenPromise` prevents multiple simultaneous refresh calls
- Backend uses `SemaphoreSlim` to prevent race conditions

## Configuration

### Frontend (constants/index.js)
```javascript
ACCESS_TOKEN_EXPIRY_MS = 180 minutes (3 hours)
SILENT_REFRESH_INTERVAL_MS = 179 minutes (refresh 1 min before expiry)
```

### Backend (appsettings.json)
```json
"Jwt": {
  "AccessTokenExpiryMinutes": 180,
  "RefreshTokenExpiryDays": 7
}
```

## Files Modified

1. `frontend/src/api/axios.js` - Memory-only token access
2. `frontend/src/store/authStore.js` - Export getter, remove ACCESS_TOKEN
3. `frontend/src/App.jsx` - Wire up authStore to axios
4. `frontend/src/components/PrivateRoute.jsx` - Check refreshToken instead
5. `frontend/src/utils/storageUtils.js` - Remove ACCESS_TOKEN from essential keys
6. `frontend/src/utils/migrateStorage.js` - NEW: Clean up deprecated tokens
