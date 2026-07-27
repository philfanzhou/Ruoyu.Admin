# AccountLinking — 设计说明 (DESIGN)

## 本功能在项目中的目录与文件结构

```
src/admin_portal/
├── backend/
│   ├── Controllers/
│   │   ├── StudentsController.cs              # 关联/解关联/查询账户 ID（HTTP）
│   │   ├── StudentAssociationsController.cs   # 查询学生关联的教师/助教（聚合）
│   │   └── IdentityAccountsController.cs      # 批量查询/反查学生（HTTP）
│   └── Models/
│       ├── LinkUserRequest.cs                  # 关联请求（record）
│       ├── IdentityAccountDto.cs               # 身份账户 DTO（record）
│       ├── StudentDto.cs                       # 学生 DTO（record，共享）
│       ├── LinkedAccountDto.cs                 # 关联教师/助教 DTO（record）
│       ├── LinkedAccountsResponse.cs           # 关联列表响应（record）
│       ├── OperationResponse.cs               # 操作结果响应（record，共享）
│       └── ErrorResponse.cs                   # 错误响应（record，共享）
├── frontend/
│   └── src/
│       ├── views/
│       │   └── StudentView.vue                # 学生管理页面（含关联教师/助教对话框）
│       ├── components/
│       │   └── StudentAssociationDrawer.vue   # 教师/助教侧管理关联学生（复用 studentLinkApi）
│       └── services/
│           ├── studentAdminApi.ts             # admin_portal 学生相关 API
│           ├── studentLinkApi.ts              # 关联学生通用 API（教师/助教/学生三视角共用）
│           ├── teacherPortalApi.ts            # 教师列表查询 API
│           └── assistantPortalApi.ts          # 助教列表查询 API
└── docs/modules/StudentManagement/AccountLinking/
    ├── 01-FEATURE.md
    ├── 02-SPEC.md
    ├── 03-DESIGN.md
    ├── 04-TASKS.md
    ├── 05-TESTS.md
    └── 06-CONVENTIONS.md
```

## 关键接口签名和数据结构定义

### StudentsController — 关联/解关联/查询账户 ID（HTTP）

```csharp
// backend/Controllers/StudentsController.cs
[Route("api/admin/students")]
[ApiController]
[Authorize]
public class StudentsController : ControllerBase
{
    public StudentsController(
        IStudentHttpClient studentClient,
        ILogger<StudentsController> logger)

    [HttpGet("{studentId:guid}/accounts")]
    public async Task<IActionResult> GetIdentityAccountsByStudentId(Guid studentId)

    [HttpPost("{studentId:guid}/accounts")]
    public async Task<IActionResult> LinkIdentityAccountToStudent(Guid studentId, [FromBody] LinkUserRequest request)

    [HttpDelete("{studentId:guid}/accounts/{accountId:guid}")]
    public async Task<IActionResult> UnlinkIdentityAccountFromStudent(Guid studentId, Guid accountId)
}
```

### StudentAssociationsController — 查询学生关联的教师/助教（聚合）

```csharp
// backend/Controllers/StudentAssociationsController.cs
[Route("api/admin/students")]
[ApiController]
[Authorize]
public class StudentAssociationsController : ControllerBase
{
    public StudentAssociationsController(
        IHttpClientFactory httpClientFactory,
        IStudentHttpClient studentClient,
        IConfiguration configuration,
        ILogger<StudentAssociationsController> logger)

    [HttpGet("{studentId:guid}/linked-accounts")]
    public async Task<IActionResult> GetLinkedAccounts(Guid studentId)
}
```

该端点先通过 `IStudentHttpClient.GetIdentityAccountsByStudentIdAsync` 取学生的 `identityAccountIds`，再分别 HTTP 调用 `TeacherPortal:Url/api/admin/teachers` 与 `AssistantPortal:Url/api/admin/assistants` 拉取全量列表，过滤出 `userId` 在学生 `identityAccountIds` 中的记录。任一下游不可用时返回对应空列表。

### IdentityAccountsController — 批量查询/反查学生（HTTP）

```csharp
// backend/Controllers/IdentityAccountsController.cs
[Route("api/admin")]
[ApiController]
[Authorize]
public class IdentityAccountsController : ControllerBase
{
    public IdentityAccountsController(
        IHttpClientFactory httpClientFactory,
        IOptions<IdentityServiceOptions> options,
        ILogger<IdentityAccountsController> logger)

    [HttpPost("identity-accounts/batch")]
    public async Task<IActionResult> GetIdentityAccountsBatch([FromBody] List<string> accountIds)

    [HttpGet("accounts/{accountId:guid}/students")]
    public async Task<IActionResult> GetStudentsByIdentityAccountId(Guid accountId)
}
```

### 数据模型

```csharp
// backend/Models/LinkUserRequest.cs
public sealed record LinkUserRequest(string IdentityAccountId);

// backend/Models/IdentityAccountDto.cs
public sealed record IdentityAccountDto(string UserId, string Username, string DisplayName, string Phone, string Remark);

// backend/Models/LinkedAccountDto.cs
public sealed record LinkedAccountDto(string UserId, string DisplayName, string Phone, string Subjects, string Role);

// backend/Models/LinkedAccountsResponse.cs
public sealed record LinkedAccountsResponse(IReadOnlyList<LinkedAccountDto> Teachers, IReadOnlyList<LinkedAccountDto> Assistants);

// 内部模型（不对外暴露）
internal sealed record IdentityUserItem(string UserId, string Username, string Phone, string Remark, string DisplayName);
```

### 前端 API 客户端 — studentLinkApi（学生/教师/助教三视角共用）

```typescript
// frontend/src/services/studentLinkApi.ts
class StudentLinkApi {
  private url(role: 'teacher' | 'assistant', userId: string): string {
    const prefix = role === 'teacher' ? 'teacher-portal' : 'assistant-portal'
    const resource = role === 'teacher' ? 'teachers' : 'assistants'
    return `/api/${prefix}/admin/${resource}/${encodeURIComponent(userId)}/students`
  }

  async list(userId: string, role: 'teacher' | 'assistant'): Promise<string[]>
  async link(userId: string, studentId: string, role: 'teacher' | 'assistant'): Promise<void>
  async unlink(userId: string, studentId: string, role: 'teacher' | 'assistant'): Promise<void>
}
```

**复用约定**：`studentLinkApi` 的 `userId` 参数始终是教师/助教的 identity account ID（即 `TeacherAccountDto.userId` / `AssistantAccountDto.userId`），`studentId` 始终是学生的 GUID。三个视角（教师管理页、助教管理页、学生管理页）的关联增删都通过此 API 调用同一组底层端点。

### 注入的外部依赖

**StudentsController**：

```csharp
public StudentsController(
    IStudentHttpClient studentClient,
    ILogger<StudentsController> logger)
```

- `IStudentHttpClient`：提供 GetIdentityAccountsByStudentIdAsync、LinkIdentityAccountToStudentAsync、UnlinkIdentityAccountFromStudentAsync、GetStudentsByIdentityAccountIdAsync 方法（HTTP）。

**StudentAssociationsController**：

```csharp
public StudentAssociationsController(
    IHttpClientFactory httpClientFactory,
    IStudentHttpClient studentClient,
    IConfiguration configuration,
    ILogger<StudentAssociationsController> logger)
```

- `IHttpClientFactory`：创建名为 "TeacherPortal" / "AssistantPortal" 的 HttpClient。
- `IStudentHttpClient`：取学生的 identityAccountIds。
- `IConfiguration`：读取 `TeacherPortal:Url` / `AssistantPortal:Url`。

**IdentityAccountsController**：

```csharp
public IdentityAccountsController(
    IHttpClientFactory httpClientFactory,
    IOptions<IdentityServiceOptions> options,
    ILogger<IdentityAccountsController> logger)
```

- `IHttpClientFactory`：创建名为 "IdentityService" 的 HttpClient，用于调用 Identity Service HTTP API。
- `IdentityServiceOptions`：包含 Address、AppId、AppSecret 配置。

## 数据流描述（步骤序列）

### 查询学生关联的身份账户 ID (GetIdentityAccountsByStudentId)

1. 接收 studentId（Guid 路由参数）。
2. 调用 `_studentClient.GetIdentityAccountsByStudentIdAsync(studentId.ToString())`。
3. 将返回的 `accountIds` 转为 `IReadOnlyList<string>` 返回 200。

### 关联身份账户到学生 (LinkIdentityAccountToStudent)

1. 接收 studentId（Guid 路由参数）和 `LinkUserRequest` body。
2. 校验：IdentityAccountId 非空白、合法 GUID 格式。
3. 校验失败返回 400 `ErrorResponse`。
4. 调用 `_studentClient.LinkIdentityAccountToStudentAsync(studentId.ToString(), request.IdentityAccountId)`。
5. 捕获 `HttpRequestException(NotFound)` → 返回 404。
6. 捕获 `HttpRequestException(BadRequest)` → 返回 400。
7. 返回 `OperationResponse(true, "Identity account linked to student successfully.")`。

### 解除身份账户关联 (UnlinkIdentityAccountFromStudent)

1. 接收 studentId 和 accountId（均为 Guid 路由参数）。
2. 调用 `_studentClient.UnlinkIdentityAccountFromStudentAsync(studentId.ToString(), accountId.ToString())`。
3. 捕获 `HttpRequestException(NotFound)` → 返回 404。
4. 捕获 `HttpRequestException(BadRequest)` → 返回 400。
5. 返回 `OperationResponse(true, "Identity account unlinked from student.")`。

### 查询学生关联的教师/助教 (GetLinkedAccounts) — 聚合查询

1. 接收 studentId（Guid 路由参数）。
2. 调用 `_studentClient.GetIdentityAccountsByStudentIdAsync(studentId.ToString())` 获取学生的 `identityAccountIds`；失败则返回空 teachers + 空 assistants。
3. 若 `accountIds.Count == 0`，返回空 teachers + 空 assistants。
4. 构建 `HashSet<string> accountIdSet`（大小写不敏感）。
5. 读取 `TeacherPortal:Url` 和 `AssistantPortal:Url` 配置。
6. 若 TeacherPortal 配置非空，调用 `FetchLinkedAccountsAsync("TeacherPortal", "{url}/api/admin/teachers", "教师", accountIdSet)`：
   - **创建 HttpRequestMessage（GET）并透传当前 HttpContext 的 `Authorization` 头**到下游请求。下游 `/api/admin/teachers` 端点要求 `[Authorize(Roles="admin")]`，不携带 JWT 会返回 401 导致聚合查询降级为空列表（此为 2026-07-27 修复的回归缺陷）。
   - 通过 `IHttpClientFactory.CreateClient("TeacherPortal")` 发送请求。
   - 解析 JSON（兼容 `{ data: [...] }` 与裸数组）。
   - 过滤出 `userId` 在 `accountIdSet` 中的记录，构造 `LinkedAccountDto` 列表。
   - 任一步失败 → 记录 Warning，返回空列表。
7. 若 AssistantPortal 配置非空，同理拉取助教列表（同样需透传 `Authorization` 头）。
8. 返回 `LinkedAccountsResponse(teachers, assistants)`。

> **安全约定**：本端点对 TeacherPortal/AssistantPortal 的调用属于"后端直连"（区别于前端经 `TeacherPortalProxyMiddleware` 的代理调用）。两条路径都必须携带调用方的 JWT：
> - 前端代理路径：`TeacherPortalProxyMiddleware.InvokeAsync` 的 header 转发循环已透传 `Authorization`。
> - 后端直连路径：`StudentAssociationsController.FetchLinkedAccountsAsync` 必须显式从 `HttpContext.Request.Headers["Authorization"]` 取值并写入出站 `HttpRequestMessage.Headers.Authorization`。

### 学生侧添加关联教师/助教（前端流程，无后端新端点）

1. 用户在学生管理页面点击"关联教师/助教"列的"管理"按钮，打开 `LinkedAccountsDialog`。
2. 前端调用 `studentAdminApi.getLinkedAccounts(studentId)` 加载已关联的教师/助教列表。
3. 用户在搜索框输入关键词（姓名/手机号/userId）。
4. 前端调用 `teacherPortalClient.getTeachers()` 或 `assistantPortalClient` 拉取全量教师/助教列表，按关键词过滤，排除已关联的，显示在"可添加"区域。
5. 用户点击某行"添加"按钮，前端调用 `studentLinkApi.link(teacherUserId, studentId, 'teacher')`（即 POST `/api/teacher-portal/admin/teachers/{userId}/students/{studentId}`）。
6. 该请求经 `TeacherPortalProxyMiddleware` 代理到 teacher_portal 的 `AddTeacherStudent` 端点，内部调用 `IStudentHttpClient.LinkIdentityAccountToStudentAsync(studentId, userId)`，把教师的 userId 写入学生的 `identityAccountIds`。
7. 成功后前端刷新已关联列表。
8. 用户点击某已关联行"移除"按钮，前端调用 `studentLinkApi.unlink(teacherUserId, studentId, 'teacher')`，流程同上但为 DELETE。

### 批量查询身份账户信息 (GetIdentityAccountsBatch)

1. 接收 `List<string> accountIds` body。
2. 若 accountIds 为空或 null → 返回 200 空列表。
3. 构建 `HashSet<string> targetIds`（大小写不敏感）。
4. 若 `_options.AppId` 或 `_options.AppSecret` 为空 → 返回 200 空列表。
5. 通过 `IHttpClientFactory.CreateClient("IdentityService")` 创建 HttpClient。
6. 构建请求：`POST {Address}/api/gateway/users/batch`，Header 添加 `X-Admin-AppId` 和 `X-Admin-AppSecret`，Body 为 accountIds JSON。
7. 发送请求，若响应非成功状态码 → 返回 200 空列表。
8. 反序列化为 `List<IdentityUserItem>`，过滤仅保留 targetIds 中存在的账户。
9. 映射为 `List<IdentityAccountDto>` 返回 200。
10. 异常捕获：`catch (Exception ex)` → 记录 Warning 日志，返回 200 空列表。

### 通过身份账户反查学生 (GetStudentsByIdentityAccountId)

1. 接收 accountId（Guid 路由参数）。
2. 调用 `_studentClient.GetStudentsByIdentityAccountIdAsync(accountId.ToString())`。
3. 将响应中的 Students 映射为 `IReadOnlyList<StudentDto>` 返回 200。

## 错误处理策略

- **参数校验**：关联操作在 Controller 层前置判空与 GUID 格式校验，无效参数返回 400。
- **HTTP 错误映射**：
  - `HttpRequestException(StatusCode.BadRequest)` → 400 Bad Request
  - `HttpRequestException(StatusCode.NotFound)` → 404 Not Found
  - 其他 `HttpRequestException` → 500（向上传播）
- **TeacherPortal/AssistantPortal 容错**：聚合查询 `GetLinkedAccounts` 中任一下游不可用（网络错误、5xx），对应角色返回空列表，不阻塞另一角色。**注意**：401 Unauthorized 不属于"正常降级"场景——出站请求必须透传调用方 JWT；若出现 401 说明透传逻辑缺失（2026-07-27 修复前的回归缺陷），不应被静默吞掉为"空列表"。
- **Identity Service 容错**：
  - AppId/AppSecret 未配置 → 降级返回空列表
  - HTTP 请求失败 → 降级返回空列表
  - 反序列化失败 → 降级返回空列表
  - 异常捕获 → 记录 Warning 日志，降级返回空列表

## 依赖的外部模块接口

| 接口 | 提供能力 | 所在模块 | 协议 |
| --- | --- | --- | --- |
| `IStudentHttpClient` | GetIdentityAccountsByStudentIdAsync, LinkIdentityAccountToStudentAsync, UnlinkIdentityAccountFromStudentAsync, GetStudentsByIdentityAccountIdAsync, GetStudentsByIdentityAccountIdAsync | `Ruoyu.Study.MistakeBff.HttpClients` | HTTP |
| Teacher Portal Admin API | GET/POST/DELETE `/api/admin/teachers/{userId}/students[/{studentId}]`，GET `/api/admin/teachers` | `teacher_portal` | HTTP（经 `TeacherPortalProxyMiddleware` 代理） |
| Assistant Portal Admin API | GET/POST/DELETE `/api/admin/assistants/{userId}/students[/{studentId}]`，GET `/api/admin/assistants` | `assistant_portal` | HTTP（经 `AssistantPortalProxyMiddleware` 代理） |
| Identity Service HTTP API | `/api/gateway/users/batch` 批量查询用户信息 | 外部服务 | HTTP |

## 可测试性设计

- **依赖注入接口化**：`IHttpClientFactory`、`IOptions<IdentityServiceOptions>`、`IStudentHttpClient` 通过构造函数注入，可被 Moq 替换。
- **HTTP Client 可 Mock**：通过 `HttpClientFactory` 创建的客户端可在测试中替换为 MockHttpMessageHandler 或 stub HttpClient。
- **容错路径可触发**：Mock HttpClient 返回非成功状态码、抛出异常等可模拟下游不可用场景。
- **过滤逻辑可验证**：传入包含不存在 ID 的列表，验证返回结果仅包含匹配的账户。
- **前端手动验证**：学生管理页面"关联教师/助教"对话框的添加/移除按钮通过浏览器手动验证；教师/助教管理页面的 `StudentAssociationDrawer` 行为保持不变（回归测试）。
