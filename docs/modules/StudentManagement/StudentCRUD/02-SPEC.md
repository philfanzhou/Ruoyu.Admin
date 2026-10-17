# StudentCRUD — Detailed Requirements Specification (SPEC)

## Feature overview and user stories

**Overview**: student CRUD is a core capability of the Admin Portal. It exposes CRUD operations over student profiles through a REST API and completes data persistence internally through gRPC calls to the Student service.

**User story 1 (administrator creates)**: as an administrator, I want to create a profile for a student by entering a name, a grade, and at least one identity account ID; the system generates the unique ID, sets the creation time, establishes the user association mapping, and returns the complete student profile.

**User story 2 (administrator queries)**: as an administrator, I want to filter the student list by name and grade, browse the results page by page, or view a single student's details directly.

**User story 3 (administrator updates)**: as an administrator, I want to change a student's name and grade, and optionally update the associated identity account list.

**User story 4 (administrator deletes)**: as an administrator, I want to delete student profiles that are no longer needed.

## Requirements list (independently testable)

- [x] FR-01 Can query the student list with pagination, filtered by name and grade.
- [x] FR-02 Can query a single student's detailed information.
- [x] FR-03 Can create a student profile; the name must not be empty, the grade must be 1-12, and at least one identity account ID must be associated.
- [x] FR-04 Can update a student profile; the name must not be empty, the grade must be 1-12, and IdentityAccountIds is optional.
- [x] FR-05 Can delete a student profile.
- [x] FR-06 Can fetch the grade options list (static 1-12 grade labels).
- [x] FR-07 Can fetch the subject options list (dynamically obtained from the Student service).
- [x] FR-08 On create, identity account IDs must be legal GUIDs; an illegal format returns 400.
- [x] FR-09 On update, when IdentityAccountIds is provided, every ID in it must be a legal GUID.
- [x] FR-10 Pagination parameters: page defaults to 1 (minimum 1), pageSize defaults to 20 (range 1-100).
- [x] FR-11 When gRPC returns InvalidArgument, the API returns 400 Bad Request.
- [x] FR-12 When gRPC returns NotFound, the API returns 404 Not Found (GetStudent scenario).
- [x] FR-13 When Update/Delete operations get Success=false from gRPC, the API returns 404 Not Found.

## Detailed acceptance criteria (automatically verifiable)

- AC-FR-01: calling `GET /api/admin/students` returns `PagedResponse<StudentDto>` containing `items`, `total`, `page`, and `pageSize`; the `name` parameter filters by name and the `grade` parameter filters by grade.
- AC-FR-02: calling `GET /api/admin/students/{studentId}` returns `StudentDto`; a nonexistent student returns 404.
- AC-FR-03: calling `POST /api/admin/students` with valid parameters returns `StudentDto` (with the generated Id, CreatedAt, UpdatedAt); an empty or whitespace name returns 400; a grade outside 1-12 returns 400; an empty or null IdentityAccountIds returns 400.
- AC-FR-04: calling `PUT /api/admin/students/{studentId}` with valid parameters returns `OperationResponse(Success=true)`; an empty name returns 400; an invalid grade returns 400; a gRPC Success=false returns 404.
- AC-FR-05: calling `DELETE /api/admin/students/{studentId}` returns `OperationResponse(Success=true)`; a gRPC Success=false returns 404.
- AC-FR-06: calling `GET /api/admin/students/grades` returns 12 `GradeOption`s, Value from 1 to 12, Label being the corresponding Chinese grade name.
- AC-FR-07: calling `GET /api/admin/students/subject-options` returns `List<SubjectOption>`, each option containing Value, Name, and DisplayName.
- AC-FR-08: on create, passing an IdentityAccountId in an illegal GUID format returns 400 with the illegal ID included in the error message.
- AC-FR-09: on update, passing an IdentityAccountId in an illegal GUID format returns 400 with the illegal ID included in the error message.
- AC-FR-10: page=0 or page=-1 is corrected to 1; pageSize=0 is corrected to 1; pageSize=200 is corrected to 100.
- AC-FR-11: when gRPC throws `RpcException(StatusCode.InvalidArgument)`, the API returns 400 Bad Request with an `ErrorResponse` body.
- AC-FR-12: when gRPC throws `RpcException(StatusCode.NotFound)` (GetStudent), the API returns 404 Not Found.
- AC-FR-13: when Update/Delete operations get `Success=false` from gRPC, the API returns 404 with an `ErrorResponse(ErrorMessage)` body.

## Non-functional requirements

- **Performance**: list-query P95 < 500ms (including the gRPC call).
- **Security**: only administrators may operate (authorization is handled by the upper Identity proxy middleware).
- **Reliability**: depends on the availability of the Student gRPC service; when gRPC is unavailable, returns 502.
- **Observability**: key errors should be logged through `ILogger`.

## Test strategy

- **Coverage target**: ≥ 80% line coverage of the CRUD-related methods in StudentsController.
- **Error paths that must be tested**: empty name, invalid grade, empty identity account list, illegal GUID format, gRPC InvalidArgument, gRPC NotFound, gRPC Success=false.
- **Test environment requirements**: unit tests with `xUnit + Moq`; the gRPC client is replaced with a Mock.
- **Prohibitions**: unit tests must not connect to a real gRPC service.
