# OpenSubjectManagement — Conventions and Standards (CONVENTIONS)

## Naming conventions

- **Namespaces**: `Admin.WebApi.Controllers`, `Admin.WebApi.Models`.
- **Class names**: DTOs use the `XxxDto` suffix; requests use `XxxRequest`; options use `XxxOption`; sub-items use `XxxItem`.
- **record types**: Models use `record` (immutable). Note that `OpenSubjectDto`, `SetOpenSubjectsRequest`, `SubjectItem`, and `SubjectOption` are `public record` (not sealed), unlike the `sealed record` of the project's other Models.
- **Method names**: Controller actions use PascalCase (for example `GetStudentOpenSubjects`, `SetStudentOpenSubjects`).
- **Private fields**: `_camelCase` with the underscore prefix (for example `_grpcClient`, `_logger`).
- **Routes**: controller-level `[Route("api/admin/students")]`, action-level `"{studentId:guid}/open-subjects"`.

## JSON serialization conventions

- **Property naming**: ASP.NET Core's default camelCase serialization strategy.
  - `OpenSubjectDto.OpenStartDate` → JSON `"openStartDate"`
  - `OpenSubjectDto.OpenEndDate` → JSON `"openEndDate"` (null in JSON means absent or null)
  - `OpenSubjectDto.IsActive` → JSON `"isActive"`
  - `SubjectItem.Subject` → JSON `"subject"`
  - `SetOpenSubjectsRequest.Subjects` → JSON `"subjects"`
  - `SubjectOption.DisplayName` → JSON `"displayName"`
- **Date format**: OpenStartDate/OpenEndDate use the `string` type in DateOnly format (for example "2024-01-01"), validated by `DateOnly.TryParse`.
- **Nullable fields**: `OpenEndDate` is `string?`; null in JSON means open-ended.

## Logging and security requirements

- **Never log sensitive information**: StudentId is logged only in error contexts.
- **Log levels**:
  - `Warning`: non-fatal errors [inferred].
  - `Error`: severe gRPC call errors [inferred].
- **Error messages**: externally returned error messages are short English descriptions containing no internal gRPC details.

## Error message format conventions

| Scenario | Message text (English) |
| --- | --- |
| Invalid subject value | `"Invalid subject value: {subject}"` |
| Empty OpenStartDate | `"Open start date is required"` |
| Illegal OpenStartDate format | `"Invalid start date format: {value}"` |
| Illegal OpenEndDate format | `"Invalid end date format: {value}"` |
| gRPC InvalidArgument | uses the gRPC-returned `Status.Detail` |
| gRPC Success=false | uses the gRPC-returned `ErrorMessage` |
| Set succeeded | `"Open subjects updated successfully."` |

> Note: SetStudentOpenSubjects returns 400 Bad Request on gRPC Success=false, unlike UpdateStudent/DeleteStudent which return 404 Not Found.

## Date handling conventions

- **Input validation**: date-string formats are validated with `DateOnly.TryParse`.
- **Output formatting**: OpenEndDate is formatted with `SubjectConstants.DateFormat` before going into the gRPC request.
- **Empty-value handling**: when OpenEndDate is null, the gRPC request passes the empty string `""`.
- **gRPC response mapping**: an empty-string OpenEndDate returned by gRPC maps to C# `null`.

## Subject validation conventions

- **Validation method**: subject values are validated with the static `SubjectConstants.IsValid(int subject)`.
- **Validation timing**: in the Controller layer's SetStudentOpenSubjects, validated one by one while iterating the SubjectItems.
- **Validation failure behavior**: returns 400 immediately without validating the remaining SubjectItems.
- **Source of subject values**: valid subject values are defined by `Ruoyu.Admin.Common.Constants.SubjectConstants`.

## Test tooling requirements

- **Framework**: `xUnit` 2.x.
- **Mocks**: `Moq` 4.x; the gRPC client is replaced with a Mock.
- **Assertion style**: the existing model tests use `FluentAssertions` (`Should().Be()`); Controller tests should stay consistent.
- **Async**: test methods must be `async Task`, calling the code under test with `await`.
- **Test naming**: `Constructor_ShouldSetPropertiesCorrectly` (model layer); the Controller layer should use `[MethodName]_[Scenario]_[ExpectedBehavior]`.
- **SubjectConstants dependency**: tests must confirm the behavior of `SubjectConstants.IsValid`; integration tests may be needed [to confirm].

## Code style

- **File-scoped namespaces**: `namespace Admin.WebApi.Controllers;` (semicolon-terminated).
- **record definitions**: multiple record definitions live in the same file, OpenSubjectDtos.cs.
- **Parameter validation**: the Controller action iterates the SubjectItem list, validating item by item; on failure return `BadRequest(new ErrorResponse(...))` immediately.
- **gRPC error handling**: `try-catch` with a `when` clause filtering `RpcException(StatusCode.InvalidArgument)`.
- **Date parsing**: use `DateOnly.TryParse` rather than `DateTime.TryParse` to accept only the date part.
- **Null checks**: OpenEndDate uses `string.IsNullOrEmpty`; OpenStartDate uses `string.IsNullOrEmpty`.
