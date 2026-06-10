# TeacherPortalProxy 任务清单

```json
[
  {
    "id": "T1",
    "title": "代理教师门户管理请求",
    "description": "管理员通过 /api/teacher-portal/admin/* 路径访问教师门户管理功能，中间件重写路径为 /api/admin/* 并转发到TeacherPortal，附加X-Admin-Key认证头",
    "endpoint": "/api/teacher-portal/admin/*",
    "method": "*"
  },
  {
    "id": "T2",
    "title": "代理教师门户认证请求",
    "description": "管理员通过 /api/teacher-portal/auth/* 路径访问教师门户认证功能，中间件重写路径为 /api/auth/* 并转发到TeacherPortal",
    "endpoint": "/api/teacher-portal/auth/*",
    "method": "*"
  },
  {
    "id": "T3",
    "title": "代理教师门户其他请求",
    "description": "管理员通过 /api/teacher-portal/* 其他路径访问教师门户功能，中间件默认重写路径为 /api/admin/* 并转发",
    "endpoint": "/api/teacher-portal/*",
    "method": "*"
  },
  {
    "id": "T4",
    "title": "处理教师门户不可用",
    "description": "当TeacherPortal不可达时，中间件返回502 Bad Gateway响应",
    "endpoint": "/api/teacher-portal/*",
    "method": "*"
  },
  {
    "id": "T5",
    "title": "处理未配置情况",
    "description": "当TeacherPortalOptions未配置时，中间件返回503 Service Unavailable响应",
    "endpoint": "/api/teacher-portal/*",
    "method": "*"
  }
]
```
