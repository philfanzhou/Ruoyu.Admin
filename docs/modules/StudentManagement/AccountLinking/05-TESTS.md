# AccountLinking — 测试计划 (TESTS)

测试工具：`xUnit + Moq + FluentAssertions`。现有模型测试文件：`test/Admin.WebApi.Tests/Models/ModelTests.cs`。

## 现有测试

### 模型构造函数测试（已实现）

| 测试类 | 测试方法 | 验证内容 |
| --- | --- | --- |
| `LinkUserRequestTests` | `Constructor_ShouldSetPropertiesCorrectly` | LinkUserRequest.IdentityAccountId 正确赋值 |
| `IdentityAccountDtoTests` | `Constructor_ShouldSetPropertiesCorrectly` | IdentityAccountDto 所有属性正确赋值 |
| `IdentityAccountDtoTests` | `Constructor_WithEmptyValues_ShouldAcceptEmptyStrings` | 所有字段为空字符串时正常工作 |

## 已实现测试

### AccountLinkingTests（StudentsController 关联/解关联）

| 测试方法 | 验证内容 |
| --- | --- |
| `GetIdentityAccountsByStudentId_Success` | UT-01 查询学生关联的身份账户 ID 成功 |
| `LinkIdentityAccountToStudent_Success` | UT-02 关联身份账户成功 |
| `LinkIdentityAccount_EmptyIdentityAccountId_Returns400` | UT-03 关联身份账户 — IdentityAccountId 为空 |
| `LinkIdentityAccount_InvalidGuid_Returns400` | UT-04 关联身份账户 — 非法 GUID 格式 |
| `LinkIdentityAccount_GrpcSuccessFalse_Returns404` | UT-05 关联身份账户 — gRPC 返回 Success=false |
| `LinkIdentityAccount_GrpcInvalidArgument_Returns400` | UT-06 关联身份账户 — gRPC InvalidArgument |
| `UnlinkIdentityAccount_Success` | UT-07 解除身份账户关联成功 |
| `UnlinkIdentityAccount_GrpcSuccessFalse_Returns404` | UT-08 解除关联 — gRPC 返回 Success=false |
| `UnlinkIdentityAccount_GrpcInvalidArgument_Returns400` | UT-09 解除关联 — gRPC InvalidArgument |
| `LinkIdentityAccount_WhitespaceOnlyIdentityAccountId_Returns400` | EX-01 IdentityAccountId 为纯空格 → 返回 400 |

### IdentityAccountsControllerTests

| 测试方法 | 验证内容 |
| --- | --- |
| `GetIdentityAccountsBatch_NullInput_ReturnsEmptyAccountsWithNoFailure` | 输入为 null 时返回空列表 |
| `GetIdentityAccountsBatch_EmptyInput_ReturnsEmptyAccountsWithNoFailure` | 输入为空列表时返回空列表 |
| `GetIdentityAccountsBatch_MissingCredentials_ReturnsEmptyAccountsWithNoFailure` | AppId 未配置时返回空列表 |
| `GetIdentityAccountsBatch_Success_ReturnsFilteredAccounts` | 批量查询成功返回过滤后的账户 |
| `GetIdentityAccountsBatch_FiltersOutUsersNotInTargetIds` | 过滤不在 targetIds 中的用户 |
| `GetIdentityAccountsBatch_CaseInsensitiveMatching` | 大小写不敏感匹配 |
| `GetIdentityAccountsBatch_NullFieldsDefaultToEmptyString` | null 字段降级为空字符串 |
| `GetIdentityAccountsBatch_NonSuccessStatusCode_ReturnsEmptyAccountsWithNoFailure` | HTTP 请求失败时返回空列表 |
| `GetIdentityAccountsBatch_NullDeserializedResponse_ReturnsEmptyAccountsWithNoFailure` | 反序列化响应为 null 时返回空列表 |
| `GetIdentityAccountsBatch_ExceptionDuringHttp_ReturnsPartialFailureWithWarning` | HTTP 异常时返回部分失败并记录警告 |
| `GetStudentsByIdentityAccountId_Success_ReturnsStudentDtos` | 通过身份账户反查学生成功 |
| `GetStudentsByIdentityAccountId_GrpcException_Propagates` | gRPC 异常向上传播 |

## 单元测试 — Given-When-Then 格式

### StudentsController 关联/解关联测试

#### UT-01 查询学生关联的身份账户 ID 成功（验证 SPEC FR-01）

- **Given**：Mock `_grpcClient.GetIdentityAccountsByStudentIdAsync` 返回包含 2 个 AccountId 的响应。
- **When**：调用 `GetIdentityAccountsByStudentId(validGuid)`。
- **Then**：返回 200，body 为 `IReadOnlyList<string>`，Count == 2。

#### UT-02 关联身份账户成功（验证 SPEC FR-02）

- **Given**：Mock `_grpcClient.LinkIdentityAccountToStudentAsync` 返回 `Success=true`。
- **When**：调用 `LinkIdentityAccountToStudent(validGuid, new LinkUserRequest(validAccountId))`。
- **Then**：返回 200，body 为 `OperationResponse(true, "Identity account linked to student successfully.")`。

#### UT-03 关联身份账户 — IdentityAccountId 为空（验证 SPEC FR-02）

- **Given**：`LinkUserRequest.IdentityAccountId` 为空字符串。
- **When**：调用 `LinkIdentityAccountToStudent`。
- **Then**：返回 400，body 为 `ErrorResponse("Identity Account ID is required.")`。

#### UT-04 关联身份账户 — 非法 GUID 格式（验证 SPEC FR-02）

- **Given**：`LinkUserRequest.IdentityAccountId` 为 "not-a-guid"。
- **When**：调用 `LinkIdentityAccountToStudent`。
- **Then**：返回 400，body 为 `ErrorResponse("Invalid Identity Account ID format. Must be a valid GUID.")`。

#### UT-05 关联身份账户 — gRPC 返回 Success=false（验证 SPEC FR-06）

- **Given**：Mock `_grpcClient.LinkIdentityAccountToStudentAsync` 返回 `Success=false, ErrorMessage="Account not found"`。
- **When**：调用 `LinkIdentityAccountToStudent`。
- **Then**：返回 404，body 为 `ErrorResponse("Account not found")`。

#### UT-06 关联身份账户 — gRPC InvalidArgument（验证 SPEC FR-07）

- **Given**：Mock `_grpcClient.LinkIdentityAccountToStudentAsync` 抛出 `RpcException(StatusCode.InvalidArgument, "Already linked")`。
- **When**：调用 `LinkIdentityAccountToStudent`。
- **Then**：返回 400，body 为 `ErrorResponse("Already linked")`。

#### UT-07 解除身份账户关联成功（验证 SPEC FR-03）

- **Given**：Mock `_grpcClient.UnlinkIdentityAccountFromStudentAsync` 返回 `Success=true`。
- **When**：调用 `UnlinkIdentityAccountFromStudent(validStudentId, validAccountId)`。
- **Then**：返回 200，body 为 `OperationResponse(true, "Identity account unlinked from student.")`。

#### UT-08 解除关联 — gRPC 返回 Success=false（验证 SPEC FR-08）

- **Given**：Mock `_grpcClient.UnlinkIdentityAccountFromStudentAsync` 返回 `Success=false, ErrorMessage="Mapping not found"`。
- **When**：调用 `UnlinkIdentityAccountFromStudent`。
- **Then**：返回 404，body 为 `ErrorResponse("Mapping not found")`。

#### UT-09 解除关联 — gRPC InvalidArgument（验证 SPEC FR-09）

- **Given**：Mock `_grpcClient.UnlinkIdentityAccountFromStudentAsync` 抛出 `RpcException(StatusCode.InvalidArgument)`。
- **When**：调用 `UnlinkIdentityAccountFromStudent`。
- **Then**：返回 400。

### IdentityAccountsController 测试

#### UT-10 批量查询身份账户成功（验证 SPEC FR-04）

- **Given**：Mock `IHttpClientFactory` 返回的 HttpClient 发送请求后获得包含 2 个用户的成功响应。
- **When**：调用 `GetIdentityAccountsBatch(["id1", "id2"])`。
- **Then**：返回 200，body 为 `List<IdentityAccountDto>`，Count == 2。

#### UT-11 批量查询 — 空 accountIds（验证 SPEC FR-10）

- **Given**：accountIds 为空列表。
- **When**：调用 `GetIdentityAccountsBatch([])`。
- **Then**：返回 200，body 为空列表。

#### UT-12 批量查询 — AppId 未配置（验证 SPEC FR-11）

- **Given**：`IdentityServiceOptions.AppId` 为空字符串。
- **When**：调用 `GetIdentityAccountsBatch(["id1"])`。
- **Then**：返回 200，body 为空列表；不发起 HTTP 请求。

#### UT-13 批量查询 — HTTP 请求失败（验证 SPEC FR-10）

- **Given**：Mock HttpClient 返回 500 状态码。
- **When**：调用 `GetIdentityAccountsBatch(["id1"])`。
- **Then**：返回 200，body 为空列表。

#### UT-14 批量查询 — 异常捕获（验证 SPEC FR-10）

- **Given**：Mock HttpClient 发送请求时抛出 `HttpRequestException`。
- **When**：调用 `GetIdentityAccountsBatch(["id1"])`。
- **Then**：返回 200，body 为空列表；ILogger 收到 Warning 日志。

#### UT-15 批量查询 — targetIds 过滤（验证 SPEC FR-12）

- **Given**：请求 `["id1", "id2"]`，但 Identity Service 返回 3 个用户（id1, id2, id3）。
- **When**：调用 `GetIdentityAccountsBatch(["id1", "id2"])`。
- **Then**：返回 200，body 仅包含 id1 和 id2 对应的 DTO。

#### UT-16 通过身份账户反查学生（验证 SPEC FR-05）

- **Given**：Mock `grpcClient.GetStudentsByIdentityAccountIdAsync` 返回包含 2 个学生的响应。
- **When**：调用 `GetStudentsByIdentityAccountId(validGuid, grpcClient)`。
- **Then**：返回 200，body 为 `IReadOnlyList<StudentDto>`，Count == 2。

## 边界和异常测试

- EX-01 IdentityAccountId 为纯空格 → 返回 400（`IsNullOrWhiteSpace` 校验）。
- EX-02 批量查询中 Identity Service 返回的用户字段为 null → DTO 中对应字段为空字符串（`?? string.Empty`）。
- EX-03 批量查询大小写不敏感匹配 → 请求 "ID1" 可匹配 Identity Service 返回的 "id1"。
- EX-04 GetStudentsByIdentityAccountId gRPC 异常 → 未被捕获，向上传播[推断]。

## 现有测试映射（到 SPEC 功能要求）

### 模型测试

| 测试方法 | 验证 SPEC 项 |
| --- | --- |
| `LinkUserRequestTests.Constructor_ShouldSetPropertiesCorrectly` | FR-02（模型层） |
| `IdentityAccountDtoTests.Constructor_ShouldSetPropertiesCorrectly` | FR-04（模型层） |
| `IdentityAccountDtoTests.Constructor_WithEmptyValues_ShouldAcceptEmptyStrings` | FR-04（模型层，null 降级为空字符串） |

### Controller 测试

| 测试方法 | 验证 SPEC 项 |
| --- | --- |
| `GetIdentityAccountsByStudentId_Success` | FR-01 |
| `LinkIdentityAccountToStudent_Success` | FR-02 |
| `LinkIdentityAccount_EmptyIdentityAccountId_Returns400` | FR-02 |
| `LinkIdentityAccount_InvalidGuid_Returns400` | FR-02 |
| `LinkIdentityAccount_GrpcSuccessFalse_Returns404` | FR-06 |
| `LinkIdentityAccount_GrpcInvalidArgument_Returns400` | FR-07 |
| `UnlinkIdentityAccount_Success` | FR-03 |
| `UnlinkIdentityAccount_GrpcSuccessFalse_Returns404` | FR-08 |
| `UnlinkIdentityAccount_GrpcInvalidArgument_Returns400` | FR-09 |
| `GetIdentityAccountsBatch_Success_ReturnsFilteredAccounts` | FR-04 |
| `GetIdentityAccountsBatch_NullInput_ReturnsEmptyAccountsWithNoFailure` | FR-10 |
| `GetIdentityAccountsBatch_MissingCredentials_ReturnsEmptyAccountsWithNoFailure` | FR-11 |
| `GetIdentityAccountsBatch_NonSuccessStatusCode_ReturnsEmptyAccountsWithNoFailure` | FR-10 |
| `GetIdentityAccountsBatch_FiltersOutUsersNotInTargetIds` | FR-12 |
| `GetStudentsByIdentityAccountId_Success_ReturnsStudentDtos` | FR-05 |
