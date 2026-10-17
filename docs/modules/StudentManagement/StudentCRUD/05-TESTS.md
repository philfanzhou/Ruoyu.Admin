# StudentCRUD — Test Plan (TESTS)

Test tooling: `xUnit + Moq + FluentAssertions`. The existing model test file: `test/Admin.WebApi.Tests/Models/ModelTests.cs`.

## Existing tests

### Model constructor tests (implemented)

| Test class | Test method | What it verifies |
| --- | --- | --- |
| `StudentDtoTests` | `Constructor_ShouldSetPropertiesCorrectly` | all StudentDto properties assigned correctly |
| `StudentDtoTests` | `Constructor_WithEmptyAccountIds_ShouldAcceptEmptyList` | works with an empty IdentityAccountIds list |
| `CreateStudentRequestTests` | `Constructor_ShouldSetPropertiesCorrectly` | CreateStudentRequest properties assigned correctly |
| `UpdateStudentRequestTests` | `Constructor_ShouldSetPropertiesCorrectly` | UpdateStudentRequest properties assigned correctly |
| `PagedResponseTests` | `Constructor_ShouldSetPropertiesCorrectly` | PagedResponse generic properties assigned correctly |
| `OperationResponseTests` | `Constructor_ShouldSetPropertiesCorrectly` | OperationResponse properties assigned correctly |
| `OperationResponseTests` | `Constructor_WithFailure_ShouldSetSuccessFalse` | set correctly when Success=false |
| `ErrorResponseTests` | `Constructor_ShouldSetMessageCorrectly` | ErrorResponse.Message assigned correctly |

## Implemented tests

### Controller tests (StudentsControllerTests)

| Test method | What it verifies |
| --- | --- |
| `ListStudents_Success_ReturnsPagedResponse` | UT-01 querying the student list succeeds |
| `ListStudents_PaginationNormalization_Page0_PageSize200` | UT-02 pagination parameter normalization |
| `ListStudents_FilterByNameAndGrade` | UT-03 filtering by name and grade |
| `GetStudent_Success` | UT-04 getting student details succeeds |
| `GetStudent_NotFound_Returns404` | UT-05 getting student details — not found |
| `CreateStudent_Success` | UT-06 creating a student succeeds |
| `CreateStudent_EmptyName_Returns400` | UT-07 creating a student — empty name |
| `CreateStudent_InvalidGrade_Returns400` | UT-08 creating a student — invalid grade |
| `CreateStudent_EmptyIdentityAccountIds_Returns400` | UT-09 creating a student — empty IdentityAccountIds |
| `CreateStudent_InvalidGuidFormat_Returns400` | UT-10 creating a student — illegal GUID format |
| `UpdateStudent_Success` | UT-11 updating a student succeeds |
| `UpdateStudent_GrpcSuccessFalse_Returns404` | UT-12 updating a student — gRPC returns Success=false |
| `DeleteStudent_Success` | UT-13 deleting a student succeeds |
| `DeleteStudent_NotFound_Returns404` | UT-14 deleting a student — not found |
| `GetGrades_Returns12Options` | UT-15 getting grade options |
| `GetSubjectOptions_Success` | UT-16 getting subject options |

### Boundary and exception tests (implemented)

| Test method | What it verifies |
| --- | --- |
| `ListStudents_PageNegativeOne_ResetsTo1` | EX-01 page=-1 corrected to 1 |
| `ListStudents_PageSizeZero_ResetsTo1` | EX-02 pageSize=0 corrected to 1 |
| `ListStudents_PageSize200_ClampedTo100` | EX-03 pageSize=200 corrected to 100 |
| `CreateStudent_WhitespaceOnlyName_Returns400` | EX-04 whitespace-only name → 400 |
| `CreateStudent_Grade13_Returns400` | EX-05 grade=13 → 400 |
| `CreateStudent_MultipleInvalidGuids_Returns400` | EX-06 some of multiple IdentityAccountIds illegal |
| `UpdateStudent_NullIdentityAccountIds_SkipsValidation` | EX-07 IdentityAccountIds=null on update → validation skipped |
| `CreateStudent_GrpcInvalidArgument_Returns400` | EX-08 gRPC throws InvalidArgument → 400 |

## Unit tests — Given-When-Then format

### UT-01 Query the student list successfully (verifies SPEC FR-01)

- **Given**: Mock `_grpcClient.ListStudentsAsync` returns a paged response with 2 students.
- **When**: `ListStudents(name: null, grade: null, page: 1, pageSize: 20)` is called.
- **Then**: returns 200 with a `PagedResponse<StudentDto>` body, Items.Count == 2, Total/Page/PageSize correct.

### UT-02 Query the student list — pagination normalization (verifies SPEC FR-10)

- **Given**: Mock `_grpcClient.ListStudentsAsync` returns an empty list.
- **When**: `ListStudents(page: 0, pageSize: 200)` is called.
- **Then**: the gRPC request has Page == 1 and PageSize == 100.

### UT-03 Query the student list — filter by name and grade (verifies SPEC FR-01)

- **Given**: Mock `_grpcClient.ListStudentsAsync` returns matching results.
- **When**: `ListStudents(name: "Zhang San", grade: 7)` is called.
- **Then**: the gRPC request has Name == "Zhang San" and Grade == Grade.Middle1.

### UT-04 Get student details successfully (verifies SPEC FR-02)

- **Given**: Mock `_grpcClient.GetStudentAsync` returns a valid StudentDto.
- **When**: `GetStudent(validGuid)` is called.
- **Then**: returns 200 with a `StudentDto` body.

### UT-05 Get student details — not found (verifies SPEC FR-12)

- **Given**: Mock `_grpcClient.GetStudentAsync` throws `RpcException(StatusCode.NotFound)`.
- **When**: `GetStudent(unknownGuid)` is called.
- **Then**: returns 404 with an `ErrorResponse` body.

### UT-06 Create a student successfully (verifies SPEC FR-03)

- **Given**: Mock `_grpcClient.CreateStudentAsync` returns a valid StudentDto.
- **When**: `CreateStudent(new CreateStudentRequest("Zhang San", 7, accountIds))` is called.
- **Then**: returns 200 with a `StudentDto` body.

### UT-07 Create a student — empty name (verifies SPEC FR-03)

- **Given**: the request Name is an empty string.
- **When**: `CreateStudent(new CreateStudentRequest("", 7, accountIds))` is called.
- **Then**: returns 400 with body `ErrorResponse("Name is required.")`.

### UT-08 Create a student — invalid grade (verifies SPEC FR-03)

- **Given**: the request Grade = 0.
- **When**: `CreateStudent(new CreateStudentRequest("Zhang San", 0, accountIds))` is called.
- **Then**: returns 400 with body `ErrorResponse("Invalid grade value.")`.

### UT-09 Create a student — empty IdentityAccountIds (verifies SPEC FR-03)

- **Given**: the request IdentityAccountIds is an empty list.
- **When**: `CreateStudent(new CreateStudentRequest("Zhang San", 7, []))` is called.
- **Then**: returns 400 with body `ErrorResponse("At least one Identity Account ID is required.")`.

### UT-10 Create a student — illegal GUID format (verifies SPEC FR-08)

- **Given**: the request IdentityAccountIds contains "not-a-guid".
- **When**: `CreateStudent` is called.
- **Then**: returns 400 with the error message containing "not-a-guid".

### UT-11 Update a student successfully (verifies SPEC FR-04)

- **Given**: Mock `_grpcClient.UpdateStudentAsync` returns `Success=true`.
- **When**: `UpdateStudent(validGuid, request)` is called.
- **Then**: returns 200 with body `OperationResponse(true, "Student updated successfully.")`.

### UT-12 Update a student — gRPC returns Success=false (verifies SPEC FR-13)

- **Given**: Mock `_grpcClient.UpdateStudentAsync` returns `Success=false, ErrorMessage="Student not found"`.
- **When**: `UpdateStudent(unknownGuid, request)` is called.
- **Then**: returns 404 with body `ErrorResponse("Student not found")`.

### UT-13 Delete a student successfully (verifies SPEC FR-05)

- **Given**: Mock `_grpcClient.DeleteStudentAsync` returns `Success=true`.
- **When**: `DeleteStudent(validGuid)` is called.
- **Then**: returns 200 with body `OperationResponse(true, "Student deleted.")`.

### UT-14 Delete a student — not found (verifies SPEC FR-13)

- **Given**: Mock `_grpcClient.DeleteStudentAsync` returns `Success=false, ErrorMessage="Student not found"`.
- **When**: `DeleteStudent(unknownGuid)` is called.
- **Then**: returns 404 with body `ErrorResponse("Student not found")`.

### UT-15 Get grade options (verifies SPEC FR-06)

- **Given**: no external dependencies.
- **When**: `GetGrades()` is called.
- **Then**: returns 200 with 12 `GradeOption`s in the body, the first Value=1 Label="Primary 1" and the last Value=12 Label="Senior 3".

### UT-16 Get subject options (verifies SPEC FR-07)

- **Given**: Mock `_grpcClient.GetAvailableSubjectsAsync` returns a subject list.
- **When**: `GetSubjectOptions()` is called.
- **Then**: returns 200 with a `List<SubjectOption>` body, each option containing Value, Name, and DisplayName.

## Boundary and exception tests

- EX-01 page=-1 corrected to 1.
- EX-02 pageSize=0 corrected to 1.
- EX-03 pageSize=200 corrected to 100.
- EX-04 whitespace-only name → 400 (`IsNullOrWhiteSpace` check).
- EX-05 grade=13 → 400.
- EX-06 some of multiple IdentityAccountIds illegal → 400 listing all illegal IDs.
- EX-07 IdentityAccountIds=null on update → validation skipped, nothing added to the gRPC request.
- EX-08 gRPC throws `RpcException(StatusCode.InvalidArgument)` → 400.

## Existing test mapping (to the SPEC requirements)

### Model tests

| Test method | SPEC items verified |
| --- | --- |
| `StudentDtoTests.Constructor_ShouldSetPropertiesCorrectly` | FR-01/FR-02/FR-03 (model layer) |
| `CreateStudentRequestTests.Constructor_ShouldSetPropertiesCorrectly` | FR-03 (model layer) |
| `UpdateStudentRequestTests.Constructor_ShouldSetPropertiesCorrectly` | FR-04 (model layer) |
| `PagedResponseTests.Constructor_ShouldSetPropertiesCorrectly` | FR-01 (model layer) |
| `OperationResponseTests.Constructor_ShouldSetPropertiesCorrectly` | FR-04/FR-05 (model layer) |
| `ErrorResponseTests.Constructor_ShouldSetMessageCorrectly` | FR-11/FR-12/FR-13 (model layer) |

### Controller tests

| Test method | SPEC items verified |
| --- | --- |
| `ListStudents_Success_ReturnsPagedResponse` | FR-01 |
| `ListStudents_PaginationNormalization_Page0_PageSize200` | FR-10 |
| `ListStudents_FilterByNameAndGrade` | FR-01 |
| `GetStudent_Success` | FR-02 |
| `GetStudent_NotFound_Returns404` | FR-12 |
| `CreateStudent_Success` | FR-03 |
| `CreateStudent_EmptyName_Returns400` | FR-03 |
| `CreateStudent_InvalidGrade_Returns400` | FR-03 |
| `CreateStudent_EmptyIdentityAccountIds_Returns400` | FR-03 |
| `CreateStudent_InvalidGuidFormat_Returns400` | FR-08 |
| `UpdateStudent_Success` | FR-04 |
| `UpdateStudent_GrpcSuccessFalse_Returns404` | FR-13 |
| `DeleteStudent_Success` | FR-05 |
| `DeleteStudent_NotFound_Returns404` | FR-13 |
| `GetGrades_Returns12Options` | FR-06 |
| `GetSubjectOptions_Success` | FR-07 |

## Authorization regression tests (implemented)

The test code lives in `Tests/Controllers/ControllerAuthorizationTests.cs`; it verifies through reflection that the controller-level `[Authorize]` attribute exists, preventing accidental removal during refactoring from breaking authorization. The default FallbackPolicy authorization policy cannot replace explicit annotation (project convention: every `/api/admin/*` controller must carry an explicit `[Authorize]`, as the extension point for a later `[Authorize(Roles = "admin")]` tightening).

| Test method | What it verifies |
| --- | --- |
| `StudentsController_HasAuthorizeAttribute` | StudentsController must carry the `[Authorize]` attribute (student CRUD is an administrator operation) |
