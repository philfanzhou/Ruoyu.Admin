# AccountLinking — Conventions and Standards (CONVENTIONS)

## Naming conventions

- **Namespaces**: `Admin.WebApi.Controllers`, `Admin.WebApi.Models`.
- **Class names**: Controllers use the `XxxController` suffix; DTOs use `XxxDto`; requests use `XxxRequest`; internal models use `internal sealed record`.
- **record types**: all Models use `record` (immutable); externally visible models use `public sealed record`, internal models `internal sealed record`.
- **Method names**: Controller actions use PascalCase with clear semantics (for example `GetIdentityAccountsByStudentId`, `LinkIdentityAccountToStudent`, `UnlinkIdentityAccountFromStudent`, `GetIdentityAccountsBatch`, `GetStudentsByIdentityAccountId`).
- **Private fields**: `_camelCase` with the underscore prefix (for example `_grpcClient`, `_httpClientFactory`, `_options`, `_logger`).
- **Routes**:
  - StudentsController: `[Route("api/admin/students")]`, action-level `"{studentId:guid}/accounts"`, `"{studentId:guid}/accounts/{accountId:guid}"`.
  - IdentityAccountsController: `[Route("api/admin")]`, action-level `"identity-accounts/batch"`, `"accounts/{accountId:guid}/students"`.

## JSON serialization conventions

- **Property naming**: ASP.NET Core's default camelCase serialization strategy.
  - `IdentityAccountDto.UserId` → JSON `"userId"`
  - `IdentityAccountDto.DisplayName` → JSON `"displayName"`
  - `LinkUserRequest.IdentityAccountId` → JSON `"identityAccountId"`
- **Identity Service API**: requests and responses also use camelCase (`userId`, `username`, `displayName`, `phone`, `remark`).

## Logging and security requirements

- **Never log sensitive information**: AppSecret must not be logged; IdentityAccountId is logged only in error contexts.
- **Log levels**:
  - `Warning`: Identity Service HTTP call failures (`_logger.LogWarning(ex, "Failed to batch-query identity accounts")`).
  - `Error`: severe gRPC call errors [inferred].
- **Authentication**: Identity Service calls authenticate with the `X-Admin-AppId` and `X-Admin-AppSecret` headers; AppSecret must never appear in logs.
- **Error messages**: externally returned error messages are short English descriptions containing no internal Identity Service details.

## Error message format conventions

| Scenario | Message text (English) |
| --- | --- |
| Empty IdentityAccountId | `"Identity Account ID is required."` |
| Illegal GUID format | `"Invalid Identity Account ID format. Must be a valid GUID."` |
| gRPC InvalidArgument | uses the gRPC-returned `Status.Detail` |
| Link succeeded | `"Identity account linked to student successfully."` |
| Unlink succeeded | `"Identity account unlinked from student."` |

## Fault-tolerance design conventions

- **Degradation strategy**: when the Identity Service is unavailable, the batch query degrades to an empty list instead of exposing the error to callers.
- **Defensive programming**: user fields returned by the Identity Service may be null; use `?? string.Empty` to degrade to empty strings.
- **Case-insensitive matching**: account ID filtering uses a HashSet with `StringComparer.OrdinalIgnoreCase`.
- **Exception catch scope**: `catch (Exception ex)` catches all exceptions, logs a Warning, then degrades the return.

## Dependency injection conventions

- **Constructor injection**: `IHttpClientFactory`, `IOptions<IdentityServiceOptions>`, and `ILogger` are injected through constructors.
- **Method-parameter injection**: the `StudentManagementGrpcServiceClient` in `GetStudentsByIdentityAccountId` is injected as a method parameter (a special pattern).
- **Named HttpClient**: use `IHttpClientFactory.CreateClient("IdentityService")` to create the named HttpClient.

## Test tooling requirements

- **Framework**: `xUnit` 2.x.
- **Mocks**: `Moq` 4.x; the gRPC client, IHttpClientFactory, and IOptions are all replaced with Mocks.
- **Assertion style**: the existing model tests use `FluentAssertions` (`Should().Be()`); Controller tests should stay consistent.
- **Async**: test methods must be `async Task`, calling the code under test with `await`.
- **Test naming**: `Constructor_ShouldSetPropertiesCorrectly` (model layer); the Controller layer should use `[MethodName]_[Scenario]_[ExpectedBehavior]`.

## Code style

- **File-scoped namespaces**: `namespace Admin.WebApi.Controllers;` (semicolon-terminated).
- **record definitions**: single-line primary constructors `public sealed record LinkUserRequest(string IdentityAccountId)`.
- **Parameter validation**: up-front validation at the start of Controller action methods; on failure return `BadRequest(new ErrorResponse(...))` immediately.
- **gRPC error handling**: `try-catch` with `when` clauses filtering on the `RpcException`'s `StatusCode`.
- **HTTP calls**: create the HttpClient through `IHttpClientFactory`; never `new HttpClient()` directly.
- **Configuration options**: inject configuration with the `IOptions<T>` pattern and access it through `.Value`.
- **Null safety**: fields returned by the Identity Service use `?? string.Empty` to guard against nulls.
