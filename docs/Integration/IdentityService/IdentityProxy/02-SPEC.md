# IdentityProxy Architecture

## Component relationships

```
┌──────────────────────────────────────────────────────────┐
│                    Admin Portal Backend                    │
│                                                            │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ IdentityProxyMiddleware                               │ │
│  │                                                       │ │
│  │  Request flow:                                        │ │
│  │  /api/identity/*  →  intercept  →  rewrite  →  forward │
│  │                         ↓                             │ │
│  │              add X-Admin-AppId                        │ │
│  │              add X-Admin-AppSecret                    │ │
│  └──────────────────────┬───────────────────────────────┘ │
│                         │                                  │
│  ┌──────────────────────┐                                 │
│  │ IdentityAccountsController │                          │
│  │ (supplementary controller) │                          │
│  └──────────────────────┘                                 │
└──────────────────────────────────────────────────────────┘
                          │
                          ▼
               ┌──────────────────────┐
               │ IdentityService      │
               │ (HTTP API)           │
               │                      │
               │ receives requests    │
               │ with auth headers    │
               │ handles user-admin   │
               │ operations            │
               └──────────────────────┘
```

## Request handling flow

1. The client sends a request to `/api/identity/*`
2. IdentityProxyMiddleware intercepts the request
3. Path rewrite: remove the `/api/identity` prefix, replacing it with `/api`
4. Create the HTTP request to the IdentityService
5. Add the authentication headers: `X-Admin-AppId` and `X-Admin-AppSecret`
6. Forward the request to the IdentityService
7. Return the response to the client
8. If the IdentityService is unreachable → return 502 Bad Gateway

## Path rewriting rules

| Original path | Rewritten path |
|----------|-----------|
| `/api/identity/users` | `/api/users` |
| `/api/identity/roles` | `/api/roles` |
| `/api/identity/accounts/login` | `/api/accounts/login` |

## Configuration options

### IdentityServiceOptions

| Property | Type | Description |
|------|------|------|
| Address | string | the IdentityService's base address |
| AppId | string | the admin console's application ID |
| AppSecret | string | the admin console's application secret |

## IdentityAccountsController behavior

### POST /api/admin/identity-accounts/batch

Batch-query Identity account information.

**Request body**: `List<string>` — the account ID list

**Response body**:
```json
{
  "accounts": [
    { "userId": "...", "username": "...", "displayName": "...", "phone": "...", "remark": "..." }
  ],
  "partialFailure": false,
  "warning": null
}
```

| Field | Type | Description |
|------|------|------|
| accounts | IdentityAccountDto[] | the matching account list (containing only the userIds present in the request) |
| partialFailure | bool | whether the Identity service was unavailable |
| warning | string? | the notice when unavailable, for example "The Identity service is temporarily unavailable; some account information could not be loaded" |

**Behavior rules**:
1. null or empty-list input → returns empty accounts, partialFailure=false
2. AppId or AppSecret not configured → returns empty accounts, partialFailure=false
3. The Identity service returns a non-success status code → returns empty accounts, partialFailure=false
4. The Identity service returns a null response body → returns empty accounts, partialFailure=false
5. The Identity service is available and succeeds → returns the filtered accounts (only the userIds present in the request, case-insensitive), partialFailure=false
6. An HTTP call exception → returns empty accounts, partialFailure=true, warning="The Identity service is temporarily unavailable; some account information could not be loaded"
7. Null fields of IdentityUserItem map to empty strings by default

### GET /api/admin/accounts/{accountId}/students

Query the associated students by Identity account ID.

**Response body**: `IReadOnlyList<StudentDto>`

## Service dependencies

| Service | Protocol | Purpose | Necessity |
|------|------|------|--------|
| IdentityService | HTTP | user account management | required |
