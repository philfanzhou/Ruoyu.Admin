# TeacherPortalProxy 架构

## 组件关系

```
┌──────────────────────────────────────────────────────────┐
│                    Admin Portal Backend                    │
│                                                            │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ TeacherPortalProxyMiddleware                          │ │
│  │                                                       │ │
│  │  请求流程:                                            │ │
│  │  /api/teacher-portal/*  →  拦截  →  路径重写  →  转发 │ │
│  │                              ↓                        │ │
│  │                   添加 X-Admin-Key                    │ │
│  │                                                       │ │
│  │  路径重写规则:                                        │ │
│  │  /api/teacher-portal/admin/* → /api/admin/*          │ │
│  │  /api/teacher-portal/auth/*  → /api/auth/*           │ │
│  │  /api/teacher-portal/其他    → /api/admin/*          │ │
│  └──────────────────────┬───────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
                         │
                         ▼
              ┌──────────────────────┐
              │ TeacherPortal        │
              │ (HTTP API)           │
              │                      │
              │ 接收带Admin-Key的请求 │
              │ 处理教师管理操作      │
              └──────────────────────┘
```

## 请求处理流程

1. 客户端发送请求到 `/api/teacher-portal/*`
2. TeacherPortalProxyMiddleware 拦截请求
3. 检查配置是否可用（TeacherPortalOptions），未配置 → 返回 503
4. 根据路径规则重写路径
5. 创建到 TeacherPortal 的 HTTP 请求
6. 添加认证头：`X-Admin-Key`
7. 转发请求到 TeacherPortal
8. 将响应返回给客户端
9. 若 TeacherPortal 不可达 → 返回 502 Bad Gateway

## 路径重写规则

| 原始路径 | 重写后路径 | 说明 |
|----------|-----------|------|
| `/api/teacher-portal/admin/teachers` | `/api/admin/teachers` | 管理接口 |
| `/api/teacher-portal/auth/login` | `/api/auth/login` | 认证接口 |
| `/api/teacher-portal/other` | `/api/admin/other` | 默认路由到admin |

**注意**：未匹配 admin/auth 前缀的其他路径默认路由到 `/api/admin`，这意味着教师门户的所有非认证接口都归类为管理接口。

## 配置选项

### TeacherPortalOptions

| 属性 | 类型 | 说明 |
|------|------|------|
| Address | string | TeacherPortal 的基础地址 |
| AdminApiKey | string | 管理后台的API密钥 |

## 依赖服务

| 服务 | 协议 | 用途 | 必要性 |
|------|------|------|--------|
| TeacherPortal | HTTP | 教师门户管理功能 | 必须 |
