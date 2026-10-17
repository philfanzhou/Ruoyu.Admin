# StudentCRUD — Design Notes (DESIGN)

## Directory and file structure of this capability in the project

```

├── backend/
│   ├── Controllers/
│   │   └── StudentsController.cs          # API controller (CRUD + GetGrades + GetSubjectOptions)
│   └── Models/
│       ├── StudentDto.cs                   # student DTO (record)
│       ├── CreateStudentRequest.cs         # create request (record)
│       ├── UpdateStudentRequest.cs         # update request (record)
│       ├── GradeOption.cs                  # grade option (record)
│       ├── SubjectOption.cs                # subject option (record) [inferred, defined in OpenSubjectDtos.cs]
│       ├── PagedResponse.cs               # paged response generic (record)
│       ├── OperationResponse.cs           # operation-result response (record)
│       └── ErrorResponse.cs               # error response (record)
├── test/
│   └── Admin.WebApi.Tests/
│       └── Models/
│           └── ModelTests.cs              # model constructor tests
└── docs/modules/StudentManagement/StudentCRUD/
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
[Route("api/admin/students")]
[ApiController]
public class StudentsController : ControllerBase
{
    public StudentsController(
        StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient,
        ILogger<StudentsController> logger)

    [HttpGet]
    public async Task<IActionResult> ListStudents(string? name, int? grade, int? page, int? pageSize)

    [HttpGet("grades")]
    public IActionResult GetGrades()

    [HttpGet("{studentId:guid}")]
    public async Task<IActionResult> GetStudent(Guid studentId)

    [HttpPost]
    public async Task<IActionResult> CreateStudent([FromBody] CreateStudentRequest request)

    [HttpPut("{studentId:guid}")]
    public async Task<IActionResult> UpdateStudent(Guid studentId, [FromBody] UpdateStudentRequest request)

    [HttpDelete("{studentId:guid}")]
    public async Task<IActionResult> DeleteStudent(Guid studentId)

    [HttpGet("subject-options")]
    public async Task<IActionResult> GetSubjectOptions()
}
```

### Data models

```csharp
// backend/Models/StudentDto.cs
public sealed record StudentDto(
    string Id,
    string Name,
    int Grade,
    IReadOnlyList<string> IdentityAccountIds,
    long CreatedAt,
    long UpdatedAt);

// backend/Models/CreateStudentRequest.cs
public sealed record CreateStudentRequest(string Name, int Grade, List<string> IdentityAccountIds);

// backend/Models/UpdateStudentRequest.cs
public sealed record UpdateStudentRequest(string Name, int Grade, List<string>? IdentityAccountIds);

// backend/Models/GradeOption.cs
public sealed record GradeOption(int Value, string Label);

// backend/Models/PagedResponse.cs
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// backend/Models/OperationResponse.cs
public sealed record OperationResponse(bool Success, string Message);

// backend/Models/ErrorResponse.cs
public sealed record ErrorResponse(string Message);
```

### Static grade mapping

```csharp
private static readonly Dictionary<int, string> GradeLabels = new()
{
    [1] = "Primary 1",  [2] = "Primary 2",  [3] = "Primary 3",
    [4] = "Primary 4",  [5] = "Primary 5",  [6] = "Primary 6",
    [7] = "Junior 1",   [8] = "Junior 2",   [9] = "Junior 3",
    [10] = "Senior 1",  [11] = "Senior 2",  [12] = "Senior 3",
};
```

### Injected external dependencies

```csharp
// Constructor signature
public StudentsController(
    SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient,
    ILogger<StudentsController> logger)
```

- `StudentManagementGrpcServiceClient`: the Student service's gRPC client, providing ListStudents, GetStudent, CreateStudent, UpdateStudent, DeleteStudent, and GetAvailableSubjects.
- `ILogger<StudentsController>`: the logger.

## Data flow description (step sequences)

### Query the student list (ListStudents)

1. Receive the query parameters name, grade, page, and pageSize.
2. Normalize the pagination parameters: page < 1 is corrected to 1; pageSize uses `Math.Clamp(value, 1, 100)`.
3. Build the gRPC `ListStudentsRequest`: grade > 0 converts to the Grade enum, otherwise Unspecified.
4. Call `_grpcClient.ListStudentsAsync(request)`.
5. Convert each Item of the gRPC response to `StudentDto` via `ToDto`.
6. Return `PagedResponse<StudentDto>`.

### Get student details (GetStudent)

1. Receive studentId (Guid route parameter).
2. Build the gRPC `GetStudentRequest`.
3. Call `_grpcClient.GetStudentAsync(request)`.
4. Catch `RpcException(NotFound)` → return 404; catch `RpcException(InvalidArgument)` → return 400.
5. Convert the gRPC response via `ToDto` and return 200.

### Create a student (CreateStudent)

1. Receive the `CreateStudentRequest` body.
2. Validate: the name is not whitespace, the grade is valid (1-12), IdentityAccountIds is non-empty, and all IDs are legal GUIDs.
3. On validation failure return 400 `ErrorResponse`.
4. Build the gRPC `CreateStudentRequest`, passing the name after Trim.
5. Call `_grpcClient.CreateStudentAsync(grpcRequest)`.
6. Catch `RpcException(InvalidArgument)` → return 400.
7. Convert the gRPC response via `ToDto` and return 200.

### Update a student (UpdateStudent)

1. Receive studentId (Guid route parameter) and the `UpdateStudentRequest` body.
2. Validate: the name is not whitespace, the grade is valid, and when IdentityAccountIds is provided every ID is a legal GUID.
3. Build the gRPC `UpdateStudentRequest`, AddRange-ing IdentityAccountIds when non-empty.
4. Call `_grpcClient.UpdateStudentAsync(grpcRequest)`.
5. When gRPC returns `Success=false` → return 404 `ErrorResponse(ErrorMessage)`.
6. Catch `RpcException(InvalidArgument)` → return 400.
7. Return `OperationResponse(true, "Student updated successfully.")`.

### Delete a student (DeleteStudent)

1. Receive studentId (Guid route parameter).
2. Build the gRPC `DeleteStudentRequest`.
3. Call `_grpcClient.DeleteStudentAsync(request)`.
4. When gRPC returns `Success=false` → return 404 `ErrorResponse(ErrorMessage)`.
5. Catch `RpcException(InvalidArgument)` → return 400.
6. Return `OperationResponse(true, "Student deleted.")`.

### Get grade options (GetGrades)

1. Generate `List<GradeOption>` from the static dictionary `GradeLabels`.
2. Sort by Value and return 200.

### Get subject options (GetSubjectOptions)

1. Build the gRPC `Empty` request.
2. Call `_grpcClient.GetAvailableSubjectsAsync(request)`.
3. Map the Subjects of the gRPC response to `List<SubjectOption>`.
4. Return 200.

## Error handling strategy

- **Parameter validation**: up-front null and format checks in the Controller layer; invalid parameters return 400 `ErrorResponse` directly.
- **gRPC error mapping**:
  - `RpcException(StatusCode.InvalidArgument)` → 400 Bad Request
  - `RpcException(StatusCode.NotFound)` → 404 Not Found
- **gRPC business failure**: when an Update/Delete operation gets `Success=false` from gRPC, return 404 using `ErrorMessage`.
- **Uncaught gRPC exceptions**: `RpcException`s not filtered by a `when` clause propagate upward and are handled by the ASP.NET Core middleware [inferred].

## Interfaces of external modules this depends on

| Interface | Capabilities provided | Module |
| --- | --- | --- |
| `StudentManagementGrpcServiceClient` | ListStudents, GetStudent, CreateStudent, UpdateStudent, DeleteStudent, GetAvailableSubjects | `Ruoyu.Study.Student.Contract.Protos` |

## Testability design

- **Dependency-injection interfaces**: `StudentManagementGrpcServiceClient` is injected through the constructor and can be replaced by Moq [inferred; verify whether the gRPC client is mockable].
- **Static methods are testable**: `ToDto`, `IsValidGuid`, and `IsValidGrade` are private static methods, testable indirectly through the public API.
- **Failure paths are triggerable**: a Mock gRPC client throwing `RpcException` simulates the various error scenarios.
