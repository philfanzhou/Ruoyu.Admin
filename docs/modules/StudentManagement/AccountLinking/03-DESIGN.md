# AccountLinking — Design Notes (DESIGN)

## Directory and file structure of this capability in the project

```

├── backend/
│   ├── Controllers/
│   │   ├── StudentsController.cs              # link/unlink/query account IDs (HTTP)
│   │   └── IdentityAccountsController.cs      # batch query/reverse student lookup (HTTP)
│   └── Models/
│       ├── LinkUserRequest.cs                  # link request (record)
│       ├── IdentityAccountDto.cs               # identity account DTO (record)
│       ├── StudentDto.cs                       # student DTO (record, shared)
│       ├── OperationResponse.cs               # operation-result response (record, shared)
│       └── ErrorResponse.cs                   # error response (record, shared)
├── frontend/
│   └── src/
│       ├── views/
│       │   └── StudentView.vue                # student management page (with the "Linked accounts" dialog, role-agnostic)
│       └── services/
│           ├── studentAdminApi.ts             # admin_portal student APIs (including link/unlink/query accounts)
│           ├── identityApi.ts                 # Identity user search API
│           ├── teacherPortalApi.ts            # teacher list/subject management API
│           └── assistantPortalApi.ts          # assistant list/subject management API
└── docs/modules/StudentManagement/AccountLinking/
    ├── 01-FEATURE.md
    ├── 02-SPEC.md
    ├── 03-DESIGN.md
    ├── 04-TASKS.md
    ├── 05-TESTS.md
    └── 06-CONVENTIONS.md
```

## Key interface signatures and data structure definitions

### StudentsController — link/unlink/query account IDs (HTTP)

```csharp
// backend/Controllers/StudentsController.cs
[Route("api/admin/students")]
[ApiController]
[Authorize]
public class StudentsController : ControllerBase
{
    public StudentsController(
        IStudentHttpClient studentClient,
        ILogger<StudentsController> logger)

    [HttpGet("{studentId:guid}/accounts")]
    public async Task<IActionResult> GetIdentityAccountsByStudentId(Guid studentId)

    [HttpPost("{studentId:guid}/accounts")]
    public async Task<IActionResult> LinkIdentityAccountToStudent(Guid studentId, [FromBody] LinkUserRequest request)

    [HttpDelete("{studentId:guid}/accounts/{accountId:guid}")]
    public async Task<IActionResult> UnlinkIdentityAccountFromStudent(Guid studentId, Guid accountId)
}
```

### IdentityAccountsController — batch query/reverse student lookup (HTTP)

```csharp
// backend/Controllers/IdentityAccountsController.cs
[Route("api/admin")]
[ApiController]
[Authorize]
public class IdentityAccountsController : ControllerBase
{
    public IdentityAccountsController(
        IHttpClientFactory httpClientFactory,
        IOptions<IdentityServiceOptions> options,
        ILogger<IdentityAccountsController> logger)

    [HttpPost("identity-accounts/batch")]
    public async Task<IActionResult> GetIdentityAccountsBatch([FromBody] List<string> accountIds)

    [HttpGet("accounts/{accountId:guid}/students")]
    public async Task<IActionResult> GetStudentsByIdentityAccountId(Guid accountId)
}
```

### Data models

```csharp
// backend/Models/LinkUserRequest.cs
public sealed record LinkUserRequest(string IdentityAccountId);

// backend/Models/IdentityAccountDto.cs
public sealed record IdentityAccountDto(string UserId, string Username, string DisplayName, string Phone, string Remark);

// Internal model (not exposed externally)
internal sealed record IdentityUserItem(string UserId, string Username, string Phone, string Remark, string DisplayName);
```

### Frontend API client — studentAdminApi (student-side association management)

```typescript
// frontend/src/services/studentAdminApi.ts
class StudentAdminApi {
  async getIdentityAccountsByStudentId(studentId: string): Promise<string[]>
  async linkIdentityAccount(studentId: string, accountId: string): Promise<OperationResponse>
  async unlinkIdentityAccount(studentId: string, accountId: string): Promise<OperationResponse>
  async getIdentityAccountsBatch(accountIds: string[]): Promise<{ accounts: IdentityAccountDto[] }>
}
```

**Calling conventions**: `studentId` is always the student's GUID; `accountId` is the Identity user's UserId (a GUID string). Association management happens only in the "Linked accounts" dialog on the student management page, without distinguishing account roles (teachers/assistants/regular users are all allowed).

### Injected external dependencies

**StudentsController**:

```csharp
public StudentsController(
    IStudentHttpClient studentClient,
    ILogger<StudentsController> logger)
```

- `IStudentHttpClient`: provides GetIdentityAccountsByStudentIdAsync, LinkIdentityAccountToStudentAsync, UnlinkIdentityAccountFromStudentAsync, and GetStudentsByIdentityAccountIdAsync (HTTP).

**IdentityAccountsController**:

```csharp
public IdentityAccountsController(
    IHttpClientFactory httpClientFactory,
    IOptions<IdentityServiceOptions> options,
    ILogger<IdentityAccountsController> logger)
```

- `IHttpClientFactory`: creates the HttpClient named "IdentityService" for calling the Identity Service HTTP API.
- `IdentityServiceOptions`: contains the Address, AppId, and AppSecret configuration.

## Data flow description (step sequences)

### Query the identity account IDs linked to a student (GetIdentityAccountsByStudentId)

1. Receive studentId (Guid route parameter).
2. Call `_studentClient.GetIdentityAccountsByStudentIdAsync(studentId.ToString())`.
3. Convert the returned `accountIds` to `IReadOnlyList<string>` and return 200.

### Link an identity account to a student (LinkIdentityAccountToStudent)

1. Receive studentId (Guid route parameter) and the `LinkUserRequest` body.
2. Validate: IdentityAccountId is not whitespace and is a legal GUID format.
3. On validation failure return 400 `ErrorResponse`.
4. Call `_studentClient.LinkIdentityAccountToStudentAsync(studentId.ToString(), request.IdentityAccountId)`.
5. Catch `HttpRequestException(NotFound)` → return 404.
6. Catch `HttpRequestException(BadRequest)` → return 400.
7. Return `OperationResponse(true, "Identity account linked to student successfully.")`.

### Unlink an identity account (UnlinkIdentityAccountFromStudent)

1. Receive studentId and accountId (both Guid route parameters).
2. Call `_studentClient.UnlinkIdentityAccountFromStudentAsync(studentId.ToString(), accountId.ToString())`.
3. Catch `HttpRequestException(NotFound)` → return 404.
4. Catch `HttpRequestException(BadRequest)` → return 400.
5. Return `OperationResponse(true, "Identity account unlinked from student.")`.

### Student-side linked-account management (frontend flow)

1. The user clicks the "Manage" button in the "Linked accounts" column of the student management page, opening the "Linked accounts" dialog.
2. The frontend calls `studentAdminApi.getIdentityAccountsByStudentId(studentId)` to load the linked account ID list, then calls `studentAdminApi.getIdentityAccountsBatch(ids)` to batch-fetch the account details (username/display name/phone number/remark).
3. The user types a keyword into the search box (username or phone number, at least 2 characters).
4. The frontend calls `identityApi.getUsers()` to search any Identity user, filters out already-linked ones, and shows them in the "addable" area.
5. The user clicks a row's "Add" button; the frontend calls `studentAdminApi.linkIdentityAccount(studentId, accountId)` (i.e. POST `/api/admin/students/{studentId}/accounts`).
6. On success the frontend refreshes the linked list.
7. The user clicks a linked row's "Remove" button; the frontend calls `studentAdminApi.unlinkIdentityAccount(studentId, accountId)` (i.e. DELETE `/api/admin/students/{studentId}/accounts/{accountId}`), the same flow but as DELETE.

### Batch-query identity account information (GetIdentityAccountsBatch)

1. Receive the `List<string> accountIds` body.
2. If accountIds is empty or null → return 200 with an empty list.
3. Build `HashSet<string> targetIds` (case-insensitive).
4. If `_options.AppId` or `_options.AppSecret` is empty → return 200 with an empty list.
5. Create the HttpClient via `IHttpClientFactory.CreateClient("IdentityService")`.
6. Build the request: `POST {Address}/api/gateway/users/batch`, adding the `X-Admin-AppId` and `X-Admin-AppSecret` headers, with the accountIds JSON as the body.
7. Send the request; if the response is a non-success status code → return 200 with an empty list.
8. Deserialize into `List<IdentityUserItem>` and filter to keep only the accounts present in targetIds.
9. Map to `List<IdentityAccountDto>` and return 200.
10. Exception handling: `catch (Exception ex)` → log a Warning and return 200 with an empty list.

### Reverse-look-up students by identity account (GetStudentsByIdentityAccountId)

1. Receive accountId (Guid route parameter).
2. Call `_studentClient.GetStudentsByIdentityAccountIdAsync(accountId.ToString())`.
3. Map the Students of the response to `IReadOnlyList<StudentDto>` and return 200.

## Error handling strategy

- **Parameter validation**: up-front null and GUID-format checks in the Controller layer for link operations; invalid parameters return 400.
- **HTTP error mapping**:
  - `HttpRequestException(StatusCode.BadRequest)` → 400 Bad Request
  - `HttpRequestException(StatusCode.NotFound)` → 404 Not Found
  - other `HttpRequestException` → 500 (propagates upward)
- **Identity Service fault tolerance**:
  - AppId/AppSecret not configured → degrade to an empty list
  - HTTP request failure → degrade to an empty list
  - deserialization failure → degrade to an empty list
  - exception catch → log a Warning and degrade to an empty list

## Interfaces of external modules this depends on

| Interface | Capabilities provided | Module | Protocol |
| --- | --- | --- | --- |
| `IStudentHttpClient` | GetIdentityAccountsByStudentIdAsync, LinkIdentityAccountToStudentAsync, UnlinkIdentityAccountFromStudentAsync, GetStudentsByIdentityAccountIdAsync | `Ruoyu.Admin.ServiceClients` | HTTP |
| Identity Service HTTP API | `/api/gateway/users/batch` batch user-info query, `/api/gateway/users` user search | external service | HTTP |

## Testability design

- **Dependency-injection interfaces**: `IHttpClientFactory`, `IOptions<IdentityServiceOptions>`, and `IStudentHttpClient` are constructor-injected and replaceable by Moq.
- **Mockable HTTP client**: clients created via `HttpClientFactory` can be replaced in tests with a MockHttpMessageHandler or a stub HttpClient.
- **Fault-tolerance paths are triggerable**: a Mock HttpClient returning non-success status codes or throwing exceptions simulates downstream unavailability.
- **Filtering logic is verifiable**: pass a list containing nonexistent IDs and verify the result contains only the matching accounts.
- **Manual frontend verification**: the add/remove buttons of the student management page's "Linked accounts" dialog are verified manually in the browser.
