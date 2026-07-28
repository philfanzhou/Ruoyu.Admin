# AccountLinking — 设计说明 (DESIGN)

## 本功能在项目中的目录与文件结构

```
src/admin_portal/
├── backend/
│   ├── Controllers/
│   │   ├── StudentsController.cs              # 关联/解关联/查询账户 ID（HTTP）
│   │   └── IdentityAccountsController.cs      # 批量查询/反查学生（HTTP）
│   └── Models/
│       ├── LinkUserRequest.cs                  # 关联请求（record）
│       ├── IdentityAccountDto.cs               # 身份账户 DTO（record）
│       ├── StudentDto.cs                       # 学生 DTO（record，共享）
│       ├── OperationResponse.cs               # 操作结果响应（record，共享）
│       └── ErrorResponse.cs                   # 错误响应（record，共享）
├── frontend/
│   └── src/
│       ├── views/
│       │   └── StudentView.vue                # 学生管理页面（含"关联账户"对话框，不区分角色）
│       └── services/
│           ├── studentAdminApi.ts             # admin_portal 学生相关 API（含关联/解关联/查询账户）
│           ├── identityApi.ts                 # Identity 用户搜索 API
│           ├── teacherPortalApi.ts            # 教师列表/科目管理 API
│           └── assistantPortalApi.ts          # 助教列表/科目管理 API
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

// 内部模型（不对外暴露）
internal sealed record IdentityUserItem(string UserId, string Username, string Phone, string Remark, string DisplayName);
```

### 前端 API 客户端 — studentAdminApi（学生侧关联管理）

```typescript
// frontend/src/services/studentAdminApi.ts
class StudentAdminApi {
  async getIdentityAccountsByStudentId(studentId: string): Promise<string[]>
  async linkIdentityAccount(studentId: string, accountId: string): Promise<OperationResponse>
  async unlinkIdentityAccount(studentId: string, accountId: string): Promise<OperationResponse>
  async getIdentityAccountsBatch(accountIds: string[]): Promise<{ accounts: IdentityAccountDto[] }>
}
```

**调用约定**：`studentId` 始终是学生的 GUID，`accountId` 是 Identity 用户的 UserId（GUID 字符串）。关联管理仅在学生管理页"关联账户"对话框完成，不区分账户角色（教师/助教/普通用户均可）。

### 注入的外部依赖

**StudentsController**：

```csharp
public StudentsController(
    IStudentHttpClient studentClient,
    ILogger<StudentsController> logger)
```

- `IStudentHttpClient`：提供 GetIdentityAccountsByStudentIdAsync、LinkIdentityAccountToStudentAsync、UnlinkIdentityAccountFromStudentAsync、GetStudentsByIdentityAccountIdAsync 方法（HTTP）。

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

### 学生侧管理关联账户（前端流程）

1. 用户在学生管理页面点击"关联账户"列的"管理"按钮，打开"关联账户"对话框。
2. 前端调用 `studentAdminApi.getIdentityAccountsByStudentId(studentId)` 加载已关联的账户 ID 列表，再调用 `studentAdminApi.getIdentityAccountsBatch(ids)` 批量获取账户详情（用户名/显示名/手机号/备注）。
3. 用户在搜索框输入关键词（用户名或手机号，至少 2 个字符）。
4. 前端调用 `identityApi.getUsers()` 搜索任意 Identity 用户，过滤出未关联的，显示在"可添加"区域。
5. 用户点击某行"添加"按钮，前端调用 `studentAdminApi.linkIdentityAccount(studentId, accountId)`（即 POST `/api/admin/students/{studentId}/accounts`）。
6. 成功后前端刷新已关联列表。
7. 用户点击某已关联行"移除"按钮，前端调用 `studentAdminApi.unlinkIdentityAccount(studentId, accountId)`（即 DELETE `/api/admin/students/{studentId}/accounts/{accountId}`），流程同上但为 DELETE。

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
- **Identity Service 容错**：
  - AppId/AppSecret 未配置 → 降级返回空列表
  - HTTP 请求失败 → 降级返回空列表
  - 反序列化失败 → 降级返回空列表
  - 异常捕获 → 记录 Warning 日志，降级返回空列表

## 依赖的外部模块接口

| 接口 | 提供能力 | 所在模块 | 协议 |
| --- | --- | --- | --- |
| `IStudentHttpClient` | GetIdentityAccountsByStudentIdAsync, LinkIdentityAccountToStudentAsync, UnlinkIdentityAccountFromStudentAsync, GetStudentsByIdentityAccountIdAsync | `Ruoyu.Study.MistakeBff.HttpClients` | HTTP |
| Identity Service HTTP API | `/api/gateway/users/batch` 批量查询用户信息，`/api/gateway/users` 搜索用户 | 外部服务 | HTTP |

## 可测试性设计

- **依赖注入接口化**：`IHttpClientFactory`、`IOptions<IdentityServiceOptions>`、`IStudentHttpClient` 通过构造函数注入，可被 Moq 替换。
- **HTTP Client 可 Mock**：通过 `HttpClientFactory` 创建的客户端可在测试中替换为 MockHttpMessageHandler 或 stub HttpClient。
- **容错路径可触发**：Mock HttpClient 返回非成功状态码、抛出异常等可模拟下游不可用场景。
- **过滤逻辑可验证**：传入包含不存在 ID 的列表，验证返回结果仅包含匹配的账户。
- **前端手动验证**：学生管理页面"关联账户"对话框的添加/移除按钮通过浏览器手动验证。
