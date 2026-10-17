# OpenSubjectManagement — Task List (TASKS)

> Note: this capability's code is already implemented; the tasks below are **code review and automated verification** tasks.

```json
[
  {
    "id": "REVIEW-01",
    "depends_on": [],
    "action": "Review the GetStudentOpenSubjects method in StudentsController.cs: confirm the gRPC request construction (StudentId, ActiveOnly defaulting to false) and the response mapping (an empty-string OpenEndDate becomes null) match SPEC FR-01/FR-02.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Compiles; ActiveOnly defaults to false; an empty-string OpenEndDate maps to null.",
    "notes": "The method has no try-catch; gRPC exceptions propagate upward.",
    "status": "implemented"
  },
  {
    "id": "REVIEW-02",
    "depends_on": [],
    "action": "Review the SetStudentOpenSubjects method in StudentsController.cs: confirm the parameter validation logic (SubjectConstants.IsValid, non-empty OpenStartDate, DateOnly format checks, optional OpenEndDate format check), the gRPC request construction (OpenEndDate formatting), and the 400 on gRPC Success=false match SPEC FR-03~FR-09.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Compiles; all validation paths and error handling covered.",
    "notes": "Note that SetStudentOpenSubjects returns 400 (not 404) on Success=false, unlike UpdateStudent/DeleteStudent.",
    "status": "implemented"
  },
  {
    "id": "REVIEW-03",
    "depends_on": [],
    "action": "Review the record definitions of OpenSubjectDto, SetOpenSubjectsRequest, SubjectItem, and SubjectOption in Models/OpenSubjectDtos.cs: confirm the field names and types match the SPEC.",
    "files": ["backend/Models/OpenSubjectDtos.cs"],
    "acceptance": "Compiles; the record fields match the SPEC acceptance criteria.",
    "notes": "OpenSubjectDto is a public record (not sealed), unlike the sealed records of the other Models.",
    "status": "implemented"
  },
  {
    "id": "REVIEW-04",
    "depends_on": [],
    "action": "Confirm the subject-value range of SubjectConstants.IsValid and the format string of SubjectConstants.DateFormat, verifying that the validation logic in SetStudentOpenSubjects uses the correct constants.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Compiles; SubjectConstants.IsValid covers all valid subject values.",
    "notes": "SubjectConstants is defined in the Ruoyu.Admin.Common.Constants package; confirm version consistency.",
    "status": "implemented"
  },
  {
    "id": "TEST-01",
    "depends_on": ["REVIEW-01", "REVIEW-02", "REVIEW-03", "REVIEW-04"],
    "action": "Write unit tests for StudentsController's open-subject methods: cover the GetStudentOpenSubjects happy path and the activeOnly parameter, the SetStudentOpenSubjects happy path, invalid subject value, empty OpenStartDate, illegal date formats (start and end), gRPC Success=false, and gRPC InvalidArgument.",
    "files": ["test/Admin.WebApi.Tests/Controllers/StudentsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~StudentsControllerTests",
    "notes": "There are currently no Controller-level tests; a new test file is needed. The behavior of SubjectConstants.IsValid must be Mocked.",
    "status": "todo"
  },
  {
    "id": "TEST-02",
    "depends_on": ["TEST-01"],
    "action": "Run boundary tests: an empty Subjects list skips validation, a Subjects list with multiple valid subjects, a null OpenEndDate sends an empty string in the gRPC request, and a legal-date OpenEndDate is formatted correctly.",
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
