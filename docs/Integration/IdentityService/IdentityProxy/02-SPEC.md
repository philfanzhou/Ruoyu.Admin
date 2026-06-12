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

## IdentityAccountsController 行为

### POST /api/admin/identity-accounts/batch

批量查询 Identity 账户信息。

**请求体**: `List<string>` — 账户 ID 列表

**响应体**:
```json
{
  "accounts": [
    { "userId": "...", "username": "...", "displayName": "...", "phone": "...", "remark": "..." }
  ],
  "partialFailure": false,
  "warning": null
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| accounts | IdentityAccountDto[] | 匹配的账户列表（仅包含请求中存在的 userId） |
| partialFailure | bool | Identity 服务是否不可用 |
| warning | string? | 不可用时的提示信息，如 "Identity 服务暂时不可用，部分账户信息无法加载" |

**行为规则**:
1. 输入为 null 或空列表 → 返回空 accounts，partialFailure=false
2. AppId 或 AppSecret 未配置 → 返回空 accounts，partialFailure=false
3. Identity 服务返回非成功状态码 → 返回空 accounts，partialFailure=false
4. Identity 服务返回 null 响应体 → 返回空 accounts，partialFailure=false
5. Identity 服务可用且成功 → 返回过滤后的 accounts（仅包含请求中存在的 userId，大小写不敏感），partialFailure=false
6. HTTP 调用异常 → 返回空 accounts，partialFailure=true，warning="Identity 服务暂时不可用，部分账户信息无法加载"
7. IdentityUserItem 的 null 字段默认映射为空字符串

### GET /api/admin/accounts/{accountId}/students

按 Identity 账户 ID 查询关联学生。

**响应体**: `IReadOnlyList<StudentDto>`

## 依赖服务

| 服务 | 协议 | 用途 | 必要性 |
|------|------|------|--------|
| IdentityService | HTTP | 用户账户管理 | 必须 |
