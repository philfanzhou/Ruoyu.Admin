# StudentCRUD — Conventions and Standards (CONVENTIONS)

## Naming conventions

- **Namespaces**: `Admin.WebApi.Controllers`, `Admin.WebApi.Models`.
- **Class names**: Controllers use the `XxxController` suffix; DTOs/Requests use `XxxDto`/`XxxRequest`; responses use `XxxResponse`; options use `XxxOption`.
- **record types**: all Models use `record` (immutable); some use `sealed record`.
- **Method names**: Controller actions use PascalCase corresponding to the HTTP verb semantics (for example `ListStudents`, `GetStudent`, `CreateStudent`, `UpdateStudent`, `DeleteStudent`).
- **Private fields**: `_camelCase` with the underscore prefix (for example `_grpcClient`, `_logger`).
- **Private static fields**: `PascalCase` (for example `GradeLabels`).
- **Private static methods**: `PascalCase` (for example `ToDto`, `IsValidGuid`, `IsValidGrade`).
- **Routes**: controller-level `[Route("api/admin/students")]`, action-level route segments (for example `"grades"`, `"{studentId:guid}"`).

## JSON serialization conventions

- **Property naming**: ASP.NET Core's default camelCase serialization strategy; the PascalCase properties of C# records become camelCase in JSON automatically.
  - `StudentDto.Name` → JSON `"name"`
  - `StudentDto.IdentityAccountIds` → JSON `"identityAccountIds"`
  - `PagedResponse.Total` → JSON `"total"`
  - `GradeOption.Value` → JSON `"value"`
  - `SubjectOption.DisplayName` → JSON `"displayName"`
- **Timestamp format**: `CreatedAt`/`UpdatedAt` use `long` (Unix timestamps in seconds).

## Logging and security requirements

- **Never log sensitive information**: the full identity account ID list must not be logged; StudentId is logged only in error contexts.
- **Log levels**:
  - `Information`: key operation successes (the current code logs no explicit Information entries) [inferred].
  - `Warning`: non-fatal errors.
  - `Error`: gRPC call failures.
- **Error messages**: externally returned error messages are short English descriptions containing no internal exception stacks or gRPC details.

## Error message format conventions

| Scenario | Message text (English) |
| --- | --- |
| Empty name | `"Name is required."` |
| Invalid grade | `"Invalid grade value."` |
| No identity account ID (on create) | `"At least one Identity Account ID is required."` |
| Illegal GUID format | `"Invalid Identity Account ID format: {ids}"` or `"Invalid Identity Account ID format. Must be a valid GUID."` |
| gRPC InvalidArgument | uses the gRPC-returned `Status.Detail` |
| gRPC NotFound | uses the gRPC-returned `Status.Detail` |
| Update succeeded | `"Student updated successfully."` |
| Delete succeeded | `"Student deleted."` |

## Test tooling requirements

- **Framework**: `xUnit` 2.x.
- **Mocks**: `Moq` 4.x; the gRPC client must be replaced with a Mock.
- **Assertion style**: the existing model tests use `FluentAssertions` (`Should().Be()`); Controller tests should stay consistent.
- **Async**: test methods must be `async Task` and call the code under test with `await`; `Task.Wait()` / `.Result` are forbidden.
- **Test naming**: `Constructor_ShouldSetPropertiesCorrectly` (model layer); the Controller layer should use `[MethodName]_[Scenario]_[ExpectedBehavior]`.

## Code style

- **File-scoped namespaces**: `namespace Admin.WebApi.Controllers;` (semicolon-terminated).
- **record definitions**: single-line primary constructors `public sealed record StudentDto(string Id, string Name, ...)`.
- **Parameter validation**: up-front validation at the start of Controller action methods; on failure return `BadRequest(new ErrorResponse(...))` immediately.
- **gRPC error handling**: `try-catch` with `when` clauses filtering on the `RpcException`'s `StatusCode`.
- **DTO conversion**: `private static` methods (for example `ToDto`) map gRPC responses to API models.
- **Route constraints**: GUID route parameters use the `{studentId:guid}` constraint.
- **HTTP methods**: `[HttpGet]`, `[HttpPost]`, `[HttpPut]`, `[HttpDelete]` annotated explicitly.
- **Parameter sources**: `[FromQuery]` for query parameters, `[FromBody]` for request bodies.
