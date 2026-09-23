# 助教管理 — 技术设计

## 1. Admin Portal 后端

### 1.1 AssistantPortalProxyMiddleware

- 位置: `backend/Admin.WebApi/AssistantPortalProxyMiddleware.cs`
- 模式: 与 TeacherPortalProxyMiddleware 完全对称
- 注册: Program.cs 中 `app.UseMiddleware<AssistantPortalProxyMiddleware>()`
- 配置: `AssistantPortalOptions`（Url）
- 认证: 透传调用方 `Authorization: Bearer`，下游 `[Authorize(Roles="admin")]` 校验；不再注入静态 `X-Admin-Key`
- HttpClient: 复用或新建 `IHttpClientFactory("AssistantPortal")`

### 1.2 配置变更

- `appsettings.json`: 添加 `AssistantPortal` 配置节
- `Program.cs`:
  - `builder.Services.Configure<AssistantPortalOptions>(...)`
  - `builder.Services.AddHttpClient("AssistantPortal", ...)`
  - `app.UseMiddleware<AssistantPortalProxyMiddleware>()`
  - 日志输出 AssistantPortal 地址

## 2. Assistant Portal 后端

### 2.1 AdminController 端点

保留的端点：
- `GET /api/admin/assistants` — 获取助教列表
- `POST /api/admin/identity-users/{userId}/grant-assistant` — 授予助教权限
- `POST /api/admin/identity-users/{userId}/revoke-assistant` — 撤销助教权限
- `GET/PUT/POST/DELETE /api/admin/assistants/{userId}/subjects[/{subject}]` — 科目管理
- `GET /api/admin/available-subjects` — 可用科目列表

### 2.2 数据库

`AssistantDbContext` 包含 `AssistantAccounts` 和 `AssistantSubjects` 两张表，无学生关联表。

## 3. Admin Portal 前端

### 3.1 API 客户端

`frontend/src/services/assistantPortalApi.ts`

接口与 `teacherPortalApi.ts` 对称:
- `getAssistants()`
- `grantAssistant(userId, { subjects })`
- `revokeAssistant(userId)`
- `getAssistantSubjects(userId)`
- `setAssistantSubjects(userId, subjects)`
- `addAssistantSubject(userId, subject)`
- `removeAssistantSubject(userId, subject)`
- `getAvailableSubjects()`

### 3.2 视图组件

`frontend/src/views/AssistantView.vue`

布局与 TeacherView.vue 对称:
- 主表格: 头像 / User ID / 手机号 / 用户名 / 备注 / 科目 / 创建时间 / 操作（科目、撤销）
- 授予权限弹窗: 搜索 Identity 用户 → 选择科目
- 科目管理弹窗: 批量编辑科目

### 3.3 路由

`router/index.ts` 新增:
```ts
{
  path: '/assistants',
  name: 'assistants',
  component: () => import('../views/AssistantView.vue'),
  meta: { title: '助教管理' }
}
```

侧边导航需同步添加入口。

## 4. 已移除功能

- **关联账户管理**：原设计提供 `PUT /api/admin/assistants/{userId}/user-id` 端点更换 userId。已于 2026-07-27 删除。
- **助教-学生关联**：原设计提供 `GET/POST/DELETE /api/admin/assistants/{userId}/students[/{studentId}]` 端点和 `AssistantStudent` 实体表。已于 2026-07-27 删除前后端实现。关联管理统一在学生管理页完成。
