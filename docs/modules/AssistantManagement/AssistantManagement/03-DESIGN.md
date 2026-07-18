# 助教管理 — 技术设计

## 1. Admin Portal 后端

### 1.1 AssistantPortalProxyMiddleware

- 位置: `src/admin_portal/backend/Admin.WebApi/AssistantPortalProxyMiddleware.cs`
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

## 2. 助教 Portal 后端 — 学生关联

### 2.1 数据库

新增实体 `AssistantStudent`：

```csharp
namespace AssistantWebApi.Database.Entity;

public class AssistantStudent
{
    public long Id { get; set; }
    public long AssistantId { get; set; }
    public string StudentId { get; set; } = string.Empty;  // GUID string from Student service
    public DateTimeOffset CreatedAt { get; set; }

    public AssistantAccount Assistant { get; set; } = null!;
}
```

`AssistantDbContext` 添加:
- `DbSet<AssistantStudent> AssistantStudents`
- 配置联合唯一索引: `.HasIndex(x => new { x.AssistantId, x.StudentId }).IsUnique()`

### 2.2 DbService

`AssistantDbService` 新增方法:
- `GetStudentsByUserId(userId)` — 返回关联的 studentId 列表
- `SetStudentsByUserId(userId, studentIds)` — 替换式设置
- `AddStudentByUserId(userId, studentId)` — 添加单个
- `RemoveStudentByUserId(userId, studentId)` — 删除单个
- `RemoveAllStudentsByUserId(userId)` — 撤销权限时调用

### 2.3 AdminController

新增端点:
- `GET /api/admin/assistants/{userId}/students`
- `POST /api/admin/assistants/{userId}/students` (body: `{ studentIds: string[] }` — GUID strings)
- `POST /api/admin/assistants/{userId}/students/{studentId}` (studentId = GUID string)
- `DELETE /api/admin/assistants/{userId}/students/{studentId}` (studentId = GUID string)

撤销权限时（`RevokeAssistantRole`）同步调用 `RemoveAllStudentsByUserId`。

## 3. Admin Portal 前端

### 3.1 API 客户端

`src/admin_portal/frontend/src/services/assistantPortalApi.ts`

接口与 `teacherPortalApi.ts` 对称，增加学生关联方法:
- `getAssistants()`
- `grantAssistant(userId, { subjects })`
- `revokeAssistant(userId)`
- `getAssistantSubjects(userId)`
- `setAssistantSubjects(userId, subjects)`
- `addAssistantSubject(userId, subject)`
- `removeAssistantSubject(userId, subject)`
- `getAvailableSubjects()`
- `getAssistantStudents(userId)`
- `setAssistantStudents(userId, studentIds)`
- `addAssistantStudent(userId, studentId)`
- `removeAssistantStudent(userId, studentId)`

### 3.2 视图组件

`src/admin_portal/frontend/src/views/AssistantView.vue`

布局与 TeacherView.vue 对称:
- 主表格: User ID / 手机号 / 用户名 / 科目 / 创建时间 / 操作（撤销权限、管理学生）
- 授予权限弹窗: 搜索 Identity 用户 → 选择科目
- 科目管理弹窗: 批量编辑科目
- 学生管理弹窗: 当前关联学生列表 + 搜索添加 + 移除

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

## 4. 学生数据来源

学生列表通过 Admin Portal 现有的 Student gRPC 客户端获取（已在 `StudentsController` 和 `IdentityAccountsController` 中使用），无需新增数据源。
