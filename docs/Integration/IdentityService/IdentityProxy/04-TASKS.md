# IdentityProxy Task List

```json
[
  {
    "id": "T1",
    "title": "Proxy identity-service requests",
    "description": "Administrators access identity-service functionality through /api/identity/*; the middleware automatically rewrites the path, forwards to the IdentityService, and attaches the X-Admin-AppId and X-Admin-AppSecret authentication headers",
    "endpoint": "/api/identity/*",
    "method": "*"
  },
  {
    "id": "T2",
    "title": "Handle identity-service unavailability",
    "description": "When the IdentityService is unreachable, the middleware returns a 502 Bad Gateway response",
    "endpoint": "/api/identity/*",
    "method": "*"
  },
  {
    "id": "T3",
    "title": "Manage user accounts",
    "description": "Administrators manage user accounts through IdentityAccountsController [exact functionality to confirm]",
    "endpoint": "[to confirm]",
    "method": "[to confirm]"
  }
]
```
