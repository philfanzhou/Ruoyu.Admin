# AccountLinking — Task List (TASKS)

> Note: this capability's code is already implemented; the tasks below are **code review and automated verification** tasks. FR-14/FR-15 bidirectional management (IMPL-01) is implemented.

```json
[
  {
    "id": "REVIEW-01",
    "depends_on": [],
    "action": "Review the GetIdentityAccountsByStudentId method in StudentsController.cs: confirm the HTTP request construction and the response mapping (AccountIds → IReadOnlyList<string>) match SPEC FR-01.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Compiles; the return type is IReadOnlyList<string>.",
    "notes": "The method has no try-catch; HTTP exceptions propagate upward.",
    "status": "implemented"
  },
  {
    "id": "REVIEW-02",
    "depends_on": [],
    "action": "Review the LinkIdentityAccountToStudent method in StudentsController.cs: confirm the parameter validation logic (IdentityAccountId not whitespace, legal GUID format), the 404 on HTTP NotFound, and the BadRequest catch match SPEC FR-02/FR-06/FR-07.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Compiles; all validation and error-handling paths covered.",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-03",
    "depends_on": [],
    "action": "Review the UnlinkIdentityAccountFromStudent method in StudentsController.cs: confirm the 404 on HTTP NotFound and the BadRequest catch match SPEC FR-03/FR-08/FR-09.",
    "files": ["backend/Controllers/StudentsController.cs"],
    "acceptance": "Compiles; the two Guid route parameters are passed correctly.",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-04",
    "depends_on": [],
    "action": "Review the GetIdentityAccountsBatch method in IdentityAccountsController.cs: confirm the empty-list degradation logic (empty accountIds, empty AppId/AppSecret, HTTP failure, deserialization failure, exception catch), the case-insensitive targetIds matching, and the IdentityUserItem → IdentityAccountDto mapping match SPEC FR-04/FR-10/FR-11/FR-12.",
    "files": ["backend/Controllers/IdentityAccountsController.cs"],
    "acceptance": "Compiles; every degradation path returns an empty list rather than an error.",
    "notes": "Identity Service calls authenticate with the X-Admin-AppId and X-Admin-AppSecret headers.",
    "status": "implemented"
  },
  {
    "id": "REVIEW-05",
    "depends_on": [],
    "action": "Review the GetStudentsByIdentityAccountId method in IdentityAccountsController.cs: confirm the IStudentHttpClient call, request construction, and response mapping match SPEC FR-05.",
    "files": ["backend/Controllers/IdentityAccountsController.cs"],
    "acceptance": "Compiles; returns IReadOnlyList<StudentDto>.",
    "notes": "",
    "status": "implemented"
  },
  {
    "id": "REVIEW-06",
    "depends_on": [],
    "action": "Review the record definitions of LinkUserRequest, IdentityAccountDto, LinkedAccountDto, and LinkedAccountsResponse under the Models directory: confirm the field names and types match the API documentation.",
    "files": ["backend/Models/LinkUserRequest.cs", "backend/Models/IdentityAccountDto.cs"],
    "acceptance": "Compiles; the record fields match the SPEC acceptance criteria.",
    "notes": "IdentityUserItem is an internal record, not exposed externally.",
    "status": "implemented"
  },
  {
    "id": "REVIEW-07",
    "depends_on": [],
    "action": "Review the GetLinkedAccounts method in StudentAssociationsController.cs: confirm the aggregate query logic (take the student's identityAccountIds → HTTP call to TeacherPortal/AssistantPortal → filter records whose userId is in accountIdSet) and the fault-tolerant degradation (an unavailable downstream returns an empty list) match SPEC FR-13.",
    "files": ["backend/Controllers/StudentAssociationsController.cs"],
    "acceptance": "Compiles; when any downstream is unavailable the corresponding role returns an empty list.",
    "notes": "JSON parsing is compatible with both { data: [...] } and a bare array.",
    "status": "implemented"
  },
  {
    "id": "IMPL-01",
    "depends_on": [],
    "action": "Implement bidirectional management in the 'Linked teachers/assistants' dialog of StudentView.vue: (1) add a 'Remove' button to each row of the linked teacher/assistant list, calling studentLinkApi.unlink(userId, studentId, role); (2) add a search box — after entering a keyword, call teacherPortalClient.getTeachers() / assistantPortalClient to fetch the list, filter by keyword, exclude already-linked ones, and show them in the 'addable' area; (3) clicking an addable row's 'Add' button calls studentLinkApi.link(userId, studentId, role); (4) refresh the linked list on success and show an error message on failure.",
    "files": ["frontend/src/views/StudentView.vue"],
    "acceptance": "Builds (npm run build); manual browser verification: can search teachers/assistants, can add/remove links, the list refreshes correctly after operations; StudentAssociationDrawer's existing behavior unchanged.",
    "notes": "Reuses the same endpoints as studentLinkApi and StudentAssociationDrawer; no new backend endpoints needed. The search box can lazy-load on demand (triggered at ≥1 character).",
    "status": "implemented"
  },
  {
    "id": "TEST-01",
    "depends_on": ["REVIEW-01", "REVIEW-02", "REVIEW-03"],
    "action": "Write unit tests for StudentsController's link/unlink methods: cover the GetIdentityAccountsByStudentId happy path, LinkIdentityAccountToStudent parameter validation (empty ID, illegal GUID) plus the happy path and HTTP NotFound, and UnlinkIdentityAccountFromStudent's happy path plus HTTP NotFound and BadRequest.",
    "files": ["test/Admin.WebApi.Tests/Controllers/StudentsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~StudentsControllerTests",
    "notes": "There are currently no Controller-level tests; a new test file is needed.",
    "status": "todo"
  },
  {
    "id": "TEST-02",
    "depends_on": ["REVIEW-04", "REVIEW-05"],
    "action": "Write unit tests for IdentityAccountsController: cover the GetIdentityAccountsBatch happy path, empty-accountIds degradation, unconfigured-AppId/AppSecret degradation, HTTP-failure degradation, exception-catch degradation, targetIds filtering, and the GetStudentsByIdentityAccountId happy path.",
    "files": ["test/Admin.WebApi.Tests/Controllers/IdentityAccountsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~IdentityAccountsControllerTests",
    "notes": "Needs Mocks of IHttpClientFactory and IOptions<IdentityServiceOptions>.",
    "status": "todo"
  },
  {
    "id": "TEST-03",
    "depends_on": ["REVIEW-07"],
    "action": "Write unit tests for StudentAssociationsController.GetLinkedAccounts: cover a student with no identityAccountIds, TeacherPortal unavailable, AssistantPortal unavailable, JSON compatibility with both formats, and userId filtering.",
    "files": ["test/Admin.WebApi.Tests/Controllers/StudentAssociationsControllerTests.cs"],
    "acceptance": "dotnet test --filter FullyQualifiedName~StudentAssociationsControllerTests",
    "notes": "Needs Mocks of IHttpClientFactory, IStudentHttpClient, and IConfiguration.",
    "status": "todo"
  },
  {
    "id": "BUILD-01",
    "depends_on": ["TEST-01", "TEST-02", "TEST-03", "IMPL-01"],
    "action": "Build the solution in the Release configuration and run all tests, run npm run build for the frontend, and print a coverage summary.",
    "files": ["backend/*.csproj", "backend/Tests/**/*.cs", "frontend/package.json"],
    "acceptance": "dotnet test --configuration Release; cd frontend && npm run build; must be zero warnings with no test failures.",
    "notes": "",
    "status": "todo"
  }
]
```
