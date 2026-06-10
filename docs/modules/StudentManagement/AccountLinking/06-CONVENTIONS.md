# AccountLinking — 约定与规范 (CONVENTIONS)

## 命名约定

- **命名空间**：`Admin.WebApi.Controllers`、`Admin.WebApi.Models`。
- **类名**：Controller 采用 `XxxController` 后缀；DTO 采用 `XxxDto` 后缀；请求采用 `XxxRequest` 后缀；内部模型使用 `internal sealed record`。
- **record 类型**：所有 Model 均使用 `record`（不可变），对外模型使用 `public sealed record`，内部模型使用 `internal sealed record`。
- **方法名**：Controller 动作使用 PascalCase，语义明确（如 `GetIdentityAccountsByStudentId`、`LinkIdentityAccountToStudent`、`UnlinkIdentityAccountFromStudent`、`GetIdentityAccountsBatch`、`GetStudentsByIdentityAccountId`）。
- **私有字段**：采用 `_camelCase` 下划线前缀（如 `_grpcClient`、`_httpClientFactory`、`_options`、`_logger`）。
- **路由**：
  - StudentsController：`[Route("api/admin/students")]`，动作级别 `"{studentId:guid}/accounts"`、`"{studentId:guid}/accounts/{accountId:guid}"`。
  - IdentityAccountsController：`[Route("api/admin")]`，动作级别 `"identity-accounts/batch"`、`"accounts/{accountId:guid}/students"`。

## JSON 序列化约定

- **属性命名**：ASP.NET Core 默认 camelCase 序列化策略。
  - `IdentityAccountDto.UserId` → JSON `"userId"`
  - `IdentityAccountDto.DisplayName` → JSON `"displayName"`
  - `LinkUserRequest.IdentityAccountId` → JSON `"identityAccountId"`
- **Identity Service API**：请求和响应也使用 camelCase（`userId`、`username`、`displayName`、`phone`、`remark`）。

## 日志和安全要求

- **禁止记录敏感信息**：不得在日志中记录 AppSecret；IdentityAccountId 仅在错误上下文中记录。
- **日志级别**：
  - `Warning`：Identity Service HTTP 调用失败（`_logger.LogWarning(ex, "Failed to batch-query identity accounts")`）。
  - `Error`：gRPC 调用严重错误[推断]。
- **认证**：Identity Service 调用使用 `X-Admin-AppId` 和 `X-Admin-AppSecret` Header 认证，AppSecret 不得出现在日志中。
- **错误消息**：对外返回的错误消息为英文简短描述，不包含 Identity Service 内部细节。

## 错误消息格式约定

| 场景 | 消息文本（英文） |
| --- | --- |
| IdentityAccountId 为空 | `"Identity Account ID is required."` |
| 非法 GUID 格式 | `"Invalid Identity Account ID format. Must be a valid GUID."` |
| gRPC InvalidArgument | 使用 gRPC 返回的 `Status.Detail` |
| 关联成功 | `"Identity account linked to student successfully."` |
| 解关联成功 | `"Identity account unlinked from student."` |

## 容错设计约定

- **降级策略**：Identity Service 不可用时，批量查询降级返回空列表，不向调用方暴露错误。
- **防御性编程**：Identity Service 返回的用户字段可能为 null，使用 `?? string.Empty` 降级为空字符串。
- **大小写不敏感匹配**：使用 `StringComparer.OrdinalIgnoreCase` 的 HashSet 进行账户 ID 过滤。
- **异常捕获范围**：`catch (Exception ex)` 捕获所有异常，记录 Warning 日志后降级返回。

## 依赖注入约定

- **构造函数注入**：`IHttpClientFactory`、`IOptions<IdentityServiceOptions>`、`ILogger` 通过构造函数注入。
- **方法参数注入**：`GetStudentsByIdentityAccountId` 中的 `StudentManagementGrpcServiceClient` 通过方法参数注入（特殊模式）。
- **HttpClient 命名**：使用 `IHttpClientFactory.CreateClient("IdentityService")` 创建命名 HttpClient。

## 测试工具要求

- **框架**：`xUnit` 2.x。
- **Mock**：`Moq` 4.x；gRPC Client、IHttpClientFactory、IOptions 均通过 Mock 替换。
- **断言风格**：现有模型测试使用 `FluentAssertions`（`Should().Be()`）；Controller 测试建议保持一致。
- **异步**：测试方法必须为 `async Task`，使用 `await` 调用被测代码。
- **测试命名**：`Constructor_ShouldSetPropertiesCorrectly`（模型层）；Controller 层建议使用 `[方法名]_[场景]_[预期行为]`。

## 代码风格

- **文件作用域命名空间**：`namespace Admin.WebApi.Controllers;`（分号结尾）。
- **record 定义**：单行主构造函数 `public sealed record LinkUserRequest(string IdentityAccountId)`。
- **参数校验**：在 Controller 动作方法开头进行前置校验，校验失败立即返回 `BadRequest(new ErrorResponse(...))`。
- **gRPC 错误处理**：使用 `try-catch` + `when` 子句过滤 `RpcException` 的 `StatusCode`。
- **HTTP 调用**：通过 `IHttpClientFactory` 创建 HttpClient，不直接 `new HttpClient()`。
- **配置选项**：使用 `IOptions<T>` 模式注入配置，通过 `.Value` 访问。
- **null 安全**：Identity Service 返回的字段使用 `?? string.Empty` 防御 null 值。
