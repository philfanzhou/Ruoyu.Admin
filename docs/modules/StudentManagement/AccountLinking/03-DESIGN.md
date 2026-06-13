# AccountLinking — 设计说明 (DESIGN)

## 本功能在项目中的目录与文件结构

```
src/admin_portal/
├── backend/
│   ├── Controllers/
│   │   ├── StudentsController.cs              # 关联/解关联/查询账户 ID
│   │   └── IdentityAccountsController.cs      # 批量查询/反查学生
│   └── Models/
│       ├── LinkUserRequest.cs                  # 关联请求（record）
│       ├── IdentityAccountDto.cs               # 身份账户 DTO（record）
│       ├── StudentDto.cs                       # 学生 DTO（record，共享）
│       ├── OperationResponse.cs               # 操作结果响应（record，共享）
│       └── ErrorResponse.cs                   # 错误响应（record，共享）
├── test/
│   └── Admin.WebApi.Tests/
│       └── Models/
│           └── ModelTests.cs                  # 模型构造函数测试
└── docs/modules/StudentManagement/AccountLinking/
    ├── 01-FEATURE.md
    ├── 02-SPEC.md
    ├── 03-DESIGN.md
    ├── 04-TASKS.md
    ├── 05-TESTS.md
    └── 06-CONVENTIONS.md
```

## 关键接口签名和数据结构定义

### StudentsController — 关联/解关联/查询账户 ID

```csharp
// backend/Controllers/StudentsController.cs
[HttpGet("{studentId:guid}/accounts")]
public async Task<IActionResult> GetIdentityAccountsByStudentId(Guid studentId)

[HttpPost("{studentId:guid}/accounts")]
public async Task<IActionResult> LinkIdentityAccountToStudent(Guid studentId, [FromBody] LinkUserRequest request)

[HttpDelete("{studentId:guid}/accounts/{accountId:guid}")]
public async Task<IActionResult> UnlinkIdentityAccountFromStudent(Guid studentId, Guid accountId)
```

### IdentityAccountsController — 批量查询/反查学生

```csharp
// backend/Controllers/IdentityAccountsController.cs
[Route("api/admin")]
[ApiController]
public class IdentityAccountsController : ControllerBase
{
    public IdentityAccountsController(
        IHttpClientFactory httpClientFactory,
        IOptions<IdentityServiceOptions> options,
        ILogger<IdentityAccountsController> logger)

    [HttpPost("identity-accounts/batch")]
    public async Task<IActionResult> GetIdentityAccountsBatch([FromBody] List<string> accountIds)

    [HttpGet("accounts/{accountId:guid}/students")]
    public async Task<IActionResult> GetStudentsByIdentityAccountId(
        Guid accountId,
        StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient)
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

### 注入的外部依赖

**StudentsController**：

```csharp
public StudentsController(
    SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient,
    ILogger<StudentsController> logger)
```

- `StudentManagementGrpcServiceClient`：提供 GetIdentityAccountsByStudentId、LinkIdentityAccountToStudent、UnlinkIdentityAccountFromStudent 方法。

**IdentityAccountsController**：

```csharp
public IdentityAccountsController(
    IHttpClientFactory httpClientFactory,
    IOptions<IdentityServiceOptions> options,
    ILogger<IdentityAccountsController> logger)
```

- `IHttpClientFactory`：创建名为 "IdentityService" 的 HttpClient，用于调用 Identity Service HTTP API。
- `IdentityServiceOptions`：包含 Address、AppId、AppSecret 配置。
- `ILogger<IdentityAccountsController>`：日志记录器。

**GetStudentsByIdentityAccountId 特殊注入**：

- `StudentManagementGrpcServiceClient grpcClient`：通过方法参数注入（而非构造函数），提供 GetStudentsByIdentityAccountId 方法[推断，可能通过 gRPC Client Factory 自动注入]。

## 数据流描述（步骤序列）

### 查询学生关联的身份账户 ID (GetIdentityAccountsByStudentId)

1. 接收 studentId（Guid 路由参数）。
2. 构建 gRPC `GetAccountsByStudentIdRequest`。
3. 调用 `_grpcClient.GetIdentityAccountsByStudentIdAsync(request)`。
4. 将 gRPC 响应中的 `AccountIds` 转为 `IReadOnlyList<string>` 返回 200。

### 关联身份账户到学生 (LinkIdentityAccountToStudent)

1. 接收 studentId（Guid 路由参数）和 `LinkUserRequest` body。
2. 校验：IdentityAccountId 非空白、合法 GUID 格式。
3. 校验失败返回 400 `ErrorResponse`。
4. 构建 gRPC `LinkAccountRequest`。
5. 调用 `_grpcClient.LinkIdentityAccountToStudentAsync(grpcRequest)`。
6. 若 gRPC 返回 `Success=false` → 返回 404 `ErrorResponse(ErrorMessage)`。
7. 捕获 `RpcException(InvalidArgument)` → 返回 400。
8. 返回 `OperationResponse(true, "Identity account linked to student successfully.")`。

### 解除身份账户关联 (UnlinkIdentityAccountFromStudent)

1. 接收 studentId 和 accountId（均为 Guid 路由参数）。
2. 构建 gRPC `UnlinkAccountRequest`。
3. 调用 `_grpcClient.UnlinkIdentityAccountFromStudentAsync(request)`。
4. 若 gRPC 返回 `Success=false` → 返回 404 `ErrorResponse(ErrorMessage)`。
5. 捕获 `RpcException(InvalidArgument)` → 返回 400。
6. 返回 `OperationResponse(true, "Identity account unlinked from student.")`。

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

1. 接收 accountId（Guid 路由参数）和 gRPC Client（方法参数注入）。
2. 构建 gRPC `GetStudentsByAccountIdRequest`。
3. 调用 `grpcClient.GetStudentsByIdentityAccountIdAsync(request)`。
4. 将 gRPC 响应中的 Students 映射为 `IReadOnlyList<StudentDto>` 返回 200。

## 错误处理策略

- **参数校验**：关联操作在 Controller 层前置判空与 GUID 格式校验，无效参数返回 400。
- **gRPC 错误映射**：
  - `RpcException(StatusCode.InvalidArgument)` → 400 Bad Request
  - gRPC 返回 `Success=false` → 404 Not Found（使用 ErrorMessage）
- **Identity Service 容错**：
  - AppId/AppSecret 未配置 → 降级返回空列表
  - HTTP 请求失败 → 降级返回空列表
  - 反序列化失败 → 降级返回空列表
  - 异常捕获 → 记录 Warning 日志，降级返回空列表
- **GetStudentsByIdentityAccountId**：无显式错误处理，gRPC 异常将向上传播[推断]。

## 依赖的外部模块接口

| 接口 | 提供能力 | 所在模块 | 协议 |
| --- | --- | --- | --- |
| `StudentManagementGrpcServiceClient` | GetIdentityAccountsByStudentId, LinkIdentityAccountToStudent, UnlinkIdentityAccountFromStudent, GetStudentsByIdentityAccountId | `Ruoyu.Study.Student.Contract.Protos` | gRPC |
| Identity Service HTTP API | `/api/gateway/users/batch` 批量查询用户信息 | 外部服务 | HTTP |

## 可测试性设计

- **依赖注入接口化**：`IHttpClientFactory`、`IOptions<IdentityServiceOptions>` 通过构造函数注入，可被 Moq 替换。
- **gRPC Client 可 Mock**：`StudentManagementGrpcServiceClient` 的方法可被 Moq 替换[推断]。
- **容错路径可触发**：Mock HttpClient 返回非成功状态码、抛出异常等可模拟 Identity Service 不可用场景。
- **过滤逻辑可验证**：传入包含不存在 ID 的列表，验证返回结果仅包含匹配的账户。
