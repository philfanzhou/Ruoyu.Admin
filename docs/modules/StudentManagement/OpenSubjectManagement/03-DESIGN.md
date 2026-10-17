# OpenSubjectManagement — Design Notes (DESIGN)

## Directory and file structure of this capability in the project

```

├── backend/
│   ├── Controllers/
│   │   └── StudentsController.cs              # GetStudentOpenSubjects, SetStudentOpenSubjects
│   └── Models/
│       ├── OpenSubjectDtos.cs                  # OpenSubjectDto, SetOpenSubjectsRequest, SubjectItem, SubjectOption
│       ├── OperationResponse.cs               # operation-result response (record, shared)
│       └── ErrorResponse.cs                   # error response (record, shared)
├── test/
│   └── Admin.WebApi.Tests/
│       └── Models/
│           └── ModelTests.cs                  # model constructor tests
└── docs/modules/StudentManagement/OpenSubjectManagement/
    ├── 01-FEATURE.md
    ├── 02-SPEC.md
    ├── 03-DESIGN.md
    ├── 04-TASKS.md
    ├── 05-TESTS.md
    └── 06-CONVENTIONS.md
```

## Key interface signatures and data structure definitions

### API controller

```csharp
// backend/Controllers/StudentsController.cs
[HttpGet("{studentId:guid}/open-subjects")]
public async Task<IActionResult> GetStudentOpenSubjects(Guid studentId, [FromQuery] bool? activeOnly)

[HttpPut("{studentId:guid}/open-subjects")]
public async Task<IActionResult> SetStudentOpenSubjects(Guid studentId, [FromBody] SetOpenSubjectsRequest request)
```

### Data models

```csharp
// backend/Models/OpenSubjectDtos.cs
public record OpenSubjectDto(
    string Id,
    int Subject,
    string OpenStartDate,
    string? OpenEndDate,
    bool IsActive);

public record SetOpenSubjectsRequest(List<SubjectItem> Subjects);

public record SubjectItem(int Subject, string OpenStartDate, string? OpenEndDate);

public record SubjectOption(int Value, string Name, string DisplayName);
```

### Injected external dependencies

```csharp
// Constructor signature (shared by StudentsController)
public StudentsController(
    SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient,
    ILogger<StudentsController> logger)
```

- `StudentManagementGrpcServiceClient`: provides the GetStudentOpenSubjects and SetStudentOpenSubjects methods.
- `ILogger<StudentsController>`: the logger.

### External constant dependencies

```csharp
// Ruoyu.Admin.Common.Constants.SubjectConstants
SubjectConstants.IsValid(int subject)  // checks whether a subject value is valid
SubjectConstants.DateFormat            // date format string [inferred]
```

## Data flow description (step sequences)

### Query a student's open subjects (GetStudentOpenSubjects)

1. Receive studentId (Guid route parameter) and activeOnly (bool? query parameter, default false).
2. Build the gRPC `GetStudentOpenSubjectsRequest`, setting `StudentId` and `ActiveOnly`.
3. Call `_grpcClient.GetStudentOpenSubjectsAsync(request)`.
4. Map the Subjects of the gRPC response to `List<OpenSubjectDto>`:
   - `Id` ← `s.Id`
   - `Subject` ← `s.Subject`
   - `OpenStartDate` ← `s.OpenStartDate`
   - `OpenEndDate` ← `s.OpenEndDate` (an empty string becomes null)
   - `IsActive` ← `s.IsActive`
5. Return 200 `List<OpenSubjectDto>`.

### Set a student's open subjects (SetStudentOpenSubjects)

1. Receive studentId (Guid route parameter) and the `SetOpenSubjectsRequest` body.
2. Build the gRPC `SetOpenSubjectsRequest`, setting `StudentId`.
3. If `request.Subjects` is non-null and non-empty, iterate over each SubjectItem:
   a. Validate the subject: `SubjectConstants.IsValid(subject.Subject)`; invalid returns 400.
   b. Validate OpenStartDate is non-empty: `string.IsNullOrEmpty(subject.OpenStartDate)`; empty returns 400.
   c. Validate the OpenStartDate format: `DateOnly.TryParse(subject.OpenStartDate, out _)`; illegal returns 400.
   d. Validate the OpenEndDate format (when non-empty): `DateOnly.TryParse(subject.OpenEndDate, out var parsed)`; illegal returns 400.
   e. Build the gRPC `OpenSubjectItem`, formatting OpenEndDate with `SubjectConstants.DateFormat` (when non-empty) or as an empty string.
   f. Add it to the gRPC request's Subjects list.
4. Call `_grpcClient.SetStudentOpenSubjectsAsync(grpcRequest)`.
5. When gRPC returns `Success=false` → return 400 `ErrorResponse(ErrorMessage)`.
6. Catch `RpcException(InvalidArgument)` → return 400.
7. Return `OperationResponse(true, "Open subjects updated successfully.")`.

## Error handling strategy

- **Parameter validation**: the Controller layer validates each SubjectItem item by item; a failed check returns 400 `ErrorResponse` immediately.
- **gRPC error mapping**:
  - `RpcException(StatusCode.InvalidArgument)` → 400 Bad Request
  - gRPC returns `Success=false` → 400 Bad Request (note: unlike CRUD operations, which return 404)
- **GetStudentOpenSubjects**: no explicit try-catch; gRPC exceptions propagate upward [inferred].

## Interfaces of external modules this depends on

| Interface | Capabilities provided | Module |
| --- | --- | --- |
| `StudentManagementGrpcServiceClient` | GetStudentOpenSubjects, SetStudentOpenSubjects | `Ruoyu.Study.Student.Contract.Protos` |
| `SubjectConstants` | IsValid(int), DateFormat | `Ruoyu.Admin.Common.Constants` |

## Testability design

- **Dependency-injection interfaces**: `StudentManagementGrpcServiceClient` is constructor-injected and replaceable by Moq [inferred].
- **Validation logic is indirectly testable**: `SubjectConstants.IsValid` is an external static method, tested indirectly through the Controller's public API.
- **Date parsing is testable**: `DateOnly.TryParse` is a .NET standard-library method; passing various format strings verifies the validation behavior.
- **Failure paths are triggerable**: a Mock gRPC client throwing `RpcException` or returning `Success=false` simulates the various error scenarios.
