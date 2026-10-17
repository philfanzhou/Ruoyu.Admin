# TeacherPortalProxy Task List

```json
[
  {
    "id": "T1",
    "title": "Proxy teacher-portal admin requests",
    "description": "Administrators access teacher-portal admin functionality through /api/teacher-portal/admin/*; the middleware rewrites the path to /api/admin/*, forwards to the TeacherPortal, and attaches the X-Admin-Key authentication header",
    "endpoint": "/api/teacher-portal/admin/*",
    "method": "*"
  },
  {
    "id": "T2",
    "title": "Proxy teacher-portal authentication requests",
    "description": "Administrators access teacher-portal authentication functionality through /api/teacher-portal/auth/*; the middleware rewrites the path to /api/auth/* and forwards to the TeacherPortal",
    "endpoint": "/api/teacher-portal/auth/*",
    "method": "*"
  },
  {
    "id": "T3",
    "title": "Proxy other teacher-portal requests",
    "description": "Administrators access teacher-portal functionality through other /api/teacher-portal/* paths; the middleware rewrites the path to /api/admin/* by default and forwards",
    "endpoint": "/api/teacher-portal/*",
    "method": "*"
  },
  {
    "id": "T4",
    "title": "Handle teacher-portal unavailability",
    "description": "When the TeacherPortal is unreachable, the middleware returns a 502 Bad Gateway response",
    "endpoint": "/api/teacher-portal/*",
    "method": "*"
  },
  {
    "id": "T5",
    "title": "Handle the unconfigured case",
    "description": "When TeacherPortalOptions is not configured, the middleware returns a 503 Service Unavailable response",
    "endpoint": "/api/teacher-portal/*",
    "method": "*"
  }
]
```
