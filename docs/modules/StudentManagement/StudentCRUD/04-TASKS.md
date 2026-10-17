# StudentCRUD — Task List (TASKS)

> Note: this capability's code is already implemented; the tasks below are **code review and automated verification** tasks.

```json
[
  {
    "id": "REVIEW-01",
    "depends_on": [],
    "action": "Review the ListStudents method in StudentsController.cs: confirm that the pagination normalization logic (page < 1 → 1, pageSize Clamp(1,100)), the gRPC request construction (Grade enum conversion), and the response mapping (ToDto + PagedResponse) match the SPEC.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Code reading signed off as consistent with SPEC FR-01/FR-10; dotnet build compiles.",
    "notes": "Grade 0 or negative converts to Unspecified and is not used as a filter.",
    "status": "implemented"
  },
  {
    "id": "REVIEW-02",
    "depends_on": [],
    "action": "Review the GetStudent method in StudentsController.cs: confirm the RpcException catch logic for NotFound and InvalidArgument matches SPEC FR-12/FR-11.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Compiles; the exception catch order is correct (NotFound first, InvalidArgument second).",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-03",
    "depends_on": [],
    "action": "Review the CreateStudent method in StudentsController.cs: confirm the parameter validation logic (non-whitespace name, valid grade, non-empty IdentityAccountIds, GUID format checks) matches SPEC FR-03/FR-08.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Compiles; every InvalidArgument scenario is covered.",
    "notes": "The GUID format check must list all illegal IDs.",
    "status": "implemented"
  },
  {
    "id": "REVIEW-04",
    "depends_on": [],
    "action": "Review the UpdateStudent method in StudentsController.cs: confirm the parameter validation logic (non-whitespace name, valid grade, GUID checks on the optional IdentityAccountIds) matches SPEC FR-04/FR-09; confirm that a gRPC Success=false returns 404 per SPEC FR-13.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Compiles; when IdentityAccountIds is null, validation and AddRange are skipped.",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-05",
    "depends_on": [],
    "action": "Review the DeleteStudent method in StudentsController.cs: confirm that a gRPC Success=false returns 404 per SPEC FR-13.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Compiles; InvalidArgument and Success=false are both handled correctly.",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-06",
    "depends_on": [],
    "action": "Review the GetGrades and GetSubjectOptions methods in StudentsController.cs: confirm the grade labels come from a static dictionary (1-12) and the subject options come from gRPC GetAvailableSubjects.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Compiles; GradeLabels contains 12 entries; GetSubjectOptions maps SubjectOption correctly.",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-07",
    "depends_on": [],
    "action": "Review the record definitions of StudentDto, CreateStudentRequest, UpdateStudentRequest, GradeOption, PagedResponse, OperationResponse, and ErrorResponse under the Models directory: confirm the field names and types match the API documentation.",
    "files": ["backend/Models/StudentDto.cs", "backend/Models/CreateStudentRequest.cs", "backend/Models/UpdateStudentRequest.cs", "backend/Models/GradeOption.cs", "backend/Models/PagedResponse.cs", "backend/Models/OperationResponse.cs", "backend/Models/ErrorResponse.cs"],
    "acceptance": "Compiles; the record fields match the SPEC acceptance criteria.",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "TEST-01",
    "depends_on": ["REVIEW-01", "REVIEW-02", "REVIEW-03", "REVIEW-04", "REVIEW-05", "REVIEW-06", "REVIEW-07"],
    "action": "Write unit tests for StudentsController: cover ListStudents pagination normalization, GetStudent normal/NotFound/InvalidArgument, CreateStudent parameter validation and the success path, UpdateStudent parameter validation and Success=false, DeleteStudent success and NotFound, GetGrades returning 12 options, and GetSubjectOptions mapping correctly.",
    "files": ["test/Admin.WebApi.Tests/Controllers/StudentsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~StudentsControllerTests",
    "notes": "There are currently no Controller-level tests; a new test file is needed.",
    "status": "todo"
  },
  {
    "id": "TEST-02",
    "depends_on": ["TEST-01"],
    "action": "Run boundary tests: page=0 corrected to 1, pageSize=0 corrected to 1, pageSize=200 corrected to 100, a whitespace-only name returns 400, grade=0 returns 400, grade=13 returns 400.",
    "files": ["test/Admin.WebApi.Tests/Controllers/StudentsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~StudentsControllerTests",
    "notes": "",
    "status": "todo"
  },
  {
    "id": "BUILD-01",
    "depends_on": ["TEST-01", "TEST-02"],
    "action": "Build the solution in the Release configuration and run all tests, printing a coverage summary.",
    "files": ["backend/*.csproj", "backend/Tests/**/*.cs"],
    "acceptance": "dotnet test --configuration Release",
    "notes": "Must be zero warnings with no test failures.",
    "status": "todo"
  }
]
```
