# IdentityProxy 架构

## 组件关系

```
┌──────────────────────────────────────────────────────────┐
│                    Admin Portal Backend                    │
│                                                            │
│  ┌──────────────────────────────────────────────────────┐ │
│  │ IdentityProxyMiddleware                               │ │
│  │                                                       │ │
│  │  请求流程:                                            │ │
│  │  /api/identity/*  →  拦截  →  路径重写  →  转发       │ │
│  │                         ↓                             │ │
│  │              添加 X-Admin-AppId                       │ │
│  │              添加 X-Admin-AppSecret                   │ │
│  └──────────────────────┬───────────────────────────────┘ │
│                         │                                  │
│  ┌──────────────────────┐                                 │
│  │ IdentityAccountsController │                          │
│  │ (补充控制器)          │                                 │
│  └──────────────────────┘                                 │
└──────────────────────────────────────────────────────────┘
                          │
                          ▼
               ┌──────────────────────┐
               │ IdentityService      │
               │ (HTTP API)           │
               │                      │
               │ 接收带认证头的请求    │
               │ 处理用户管理操作      │
               └──────────────────────┘
```

## 请求处理流程

1. 客户端发送请求到 `/api/identity/*`
2. IdentityProxyMiddleware 拦截请求
3. 路径重写：移除 `/api/identity` 前缀，替换为 `/api`
4. 创建到 IdentityService 的 HTTP 请求
5. 添加认证头：`X-Admin-AppId` 和 `X-Admin-AppSecret`
6. 转发请求到 IdentityService
7. 将响应返回给客户端
8. 若 IdentityService 不可达 → 返回 502 Bad Gateway

## 路径重写规则

| 原始路径 | 重写后路径 |
|----------|-----------|
| `/api/identity/users` | `/api/users` |
| `/api/identity/roles` | `/api/roles` |
| `/api/identity/accounts/login` | `/api/accounts/login` |

## 配置选项

### IdentityServiceOptions

| 属性 | 类型 | 说明 |
|------|------|------|
| Address | string | IdentityService 的基础地址 |
| AppId | string | 管理后台的应用ID |
| AppSecret | string | 管理后台的应用密钥 |

## 依赖服务

| 服务 | 协议 | 用途 | 必要性 |
|------|------|------|--------|
| IdentityService | HTTP | 用户账户管理 | 必须 |
