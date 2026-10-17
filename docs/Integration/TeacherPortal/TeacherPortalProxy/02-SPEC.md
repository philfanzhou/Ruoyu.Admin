# TeacherPortalProxy Architecture

## Component relationships

```
┌──────────────────────────────────────────────────────────┐
│                    Admin Portal Backend                    │
│                                                            │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ TeacherPortalProxyMiddleware                          │ │
│  │                                                       │ │
│  │  Request flow:                                        │ │
│  │  /api/teacher-portal/*  →  intercept  →  rewrite  →  forward │
│  │                              ↓                        │ │
│  │                   add X-Admin-Key                     │ │
│  │                                                       │ │
│  │  Path rewriting rules:                                │ │
│  │  /api/teacher-portal/admin/* → /api/admin/*          │ │
│  │  /api/teacher-portal/auth/*  → /api/auth/*           │ │
│  │  /api/teacher-portal/other    → /api/admin/*          │ │
│  └──────────────────────┬───────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
                         │
                         ▼
              ┌──────────────────────┐
              │ TeacherPortal        │
              │ (HTTP API)           │
              │                      │
              │ receives requests    │
              │ with Admin-Key       │
              │ handles teacher-admin │
              │ operations            │
              └──────────────────────┘
```

## Request handling flow

1. The client sends a request to `/api/teacher-portal/*`
2. TeacherPortalProxyMiddleware intercepts the request
3. Checks whether the configuration is available (TeacherPortalOptions); not configured → return 503
4. Rewrites the path according to the path rules
5. Creates the HTTP request to the TeacherPortal
6. Adds the authentication header: `X-Admin-Key`
7. Forwards the request to the TeacherPortal
8. Returns the response to the client
9. If the TeacherPortal is unreachable → return 502 Bad Gateway

## Path rewriting rules

| Original path | Rewritten path | Description |
|----------|-----------|------|
| `/api/teacher-portal/admin/teachers` | `/api/admin/teachers` | admin interface |
| `/api/teacher-portal/auth/login` | `/api/auth/login` | authentication interface |
| `/api/teacher-portal/other` | `/api/admin/other` | other paths default to admin |

**Note**: paths not matching the admin/auth prefixes default to `/api/admin`, which means every non-authentication interface of the teacher portal is classified as an admin interface.

## Configuration options

### TeacherPortalOptions

| Property | Type | Description |
|------|------|------|
| Address | string | the TeacherPortal's base address |
| AdminApiKey | string | the admin console's API key |

## Service dependencies

| Service | Protocol | Purpose | Necessity |
|------|------|------|--------|
| TeacherPortal | HTTP | teacher-portal admin functionality | required |
