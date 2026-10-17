# AccountLinking — Detailed Requirements Specification (SPEC)

## Feature overview and user stories

**Overview**: identity account linking lets administrators establish/remove associations between student profiles and identity accounts, and batch-fetch detailed identity account information through the Identity Service's HTTP API. It also supports looking up the list of students linked to an identity account in reverse. Association management is done uniformly in the "Linked accounts" dialog on the student management page, without distinguishing account roles (teachers/assistants/regular users are all allowed); the teacher/assistant management pages no longer offer a link-students function.

**User story 1 (administrator links an account)**: as an administrator, I want to link an identity account to a student profile so that the student can sign in to the system with that identity.

**User story 2 (administrator unlinks)**: as an administrator, I want to unlink an identity account from a student profile so that the student can no longer sign in with that identity.

**User story 3 (administrator views linked accounts)**: as an administrator, I want to view all identity account IDs linked to a student and batch-fetch these accounts' details (username, display name, phone number, etc.).

**User story 4 (administrator reverse-looks-up students)**: as an administrator, I want to find all student profiles linked to an identity account ID.

**User story 5 (unified linked-account management on the student side)**: as an administrator, I want to search any Identity user in the "Linked accounts" dialog of the student management page and add them to the student, or remove accounts already linked to the student, without switching to the teacher/assistant management pages and without distinguishing account roles.

## Requirements list (independently testable)

- [x] FR-01 Can query the list of identity account IDs linked to a student.
- [x] FR-02 Can link an identity account to a student profile; IdentityAccountId must be a legal GUID format and must not be empty.
- [x] FR-03 Can unlink an identity account from a student profile.
- [x] FR-04 Can batch-query identity account details (through the Identity Service HTTP API).
- [x] FR-05 Can reverse-look-up the list of students linked to an identity account ID.
- [x] FR-06 When linking and the Student HTTP service returns NotFound, the API returns 404.
- [x] FR-07 When linking and the Student HTTP service returns BadRequest, the API returns 400.
- [x] FR-08 When unlinking and the Student HTTP service returns NotFound, the API returns 404.
- [x] FR-09 When unlinking and the Student HTTP service returns BadRequest, the API returns 400.
- [x] FR-10 When batch-querying and the Identity Service is unavailable, an empty list is returned instead of an error.
- [x] FR-11 When batch-querying and AppId or AppSecret is not configured, an empty list is returned.
- [x] FR-12 The batch query returns only the accounts included in the request (filtered by UserId).
- [x] FR-13 The "Linked accounts" column on the student management page provides a "Manage" button; clicking it opens a dialog showing all Identity accounts linked to that student (regardless of role) and supports searching any Identity user to add/remove (calling `GET /api/admin/students/{studentId}/accounts`, `POST /api/admin/students/{studentId}/accounts`, `DELETE /api/admin/students/{studentId}/accounts/{accountId}`).

## Detailed acceptance criteria (automatically verifiable)

- AC-FR-01: calling `GET /api/admin/students/{studentId}/accounts` returns `IReadOnlyList<string>` (the account ID list).
- AC-FR-02: calling `POST /api/admin/students/{studentId}/accounts` with `LinkUserRequest(IdentityAccountId)`; an empty IdentityAccountId returns 400; an illegal GUID format returns 400; when legal and the HTTP call succeeds, returns `OperationResponse(true, "Identity account linked to student successfully.")`.
- AC-FR-03: calling `DELETE /api/admin/students/{studentId}/accounts/{accountId}` returns `OperationResponse(true, "Identity account unlinked from student.")`; an HTTP NotFound returns 404.
- AC-FR-04: calling `POST /api/admin/identity-accounts/batch` with a `List<string>` of account IDs returns `List<IdentityAccountDto>`; each DTO contains UserId, Username, DisplayName, Phone, and Remark.
- AC-FR-05: calling `GET /api/admin/accounts/{accountId}/students` returns `IReadOnlyList<StudentDto>`.
- AC-FR-13: in the "Linked accounts" dialog on the student management page:
  - Each row of the linked-account list shows a "Remove" button; clicking calls `studentAdminApi.unlinkIdentityAccount(studentId, accountId)` (DELETE `/api/admin/students/{studentId}/accounts/{accountId}`), removing it from the list on success.
  - Typing a username or phone number into the search box (at least 2 characters) calls `identityApi.getUsers()` to search any Identity user, filters out already-linked accounts, and shows an "Add" button on each row; clicking calls `studentAdminApi.linkIdentityAccount(studentId, accountId)` (POST `/api/admin/students/{studentId}/accounts`), adding it to the linked list on success.
  - On operation failure an error message is shown and the dialog stays open.
- AC-FR-06: when a link operation gets NotFound from the Student HTTP service, the API returns 404 `ErrorResponse(ErrorMessage)`.
- AC-FR-07: when a link operation gets BadRequest from the Student HTTP service, the API returns 400.
- AC-FR-08: when an unlink operation gets NotFound from the Student HTTP service, the API returns 404 `ErrorResponse(ErrorMessage)`.
- AC-FR-09: when an unlink operation gets BadRequest from the Student HTTP service, the API returns 400.
- AC-FR-10: when the Identity Service HTTP call fails, the batch query returns the empty list `[]`.
- AC-FR-11: when `_options.AppId` or `_options.AppSecret` is empty, the batch query returns the empty list `[]`.
- AC-FR-12: the batch query result contains only accounts present in the requested ID list (case-insensitive matching).

## Non-functional requirements

- **Performance**: batch-query P95 < 1s (including the Identity Service HTTP call).
- **Security**: only administrators may operate; Identity Service calls authenticate with AppId/AppSecret.
- **Reliability**: an unavailable Identity Service does not affect other capabilities; the batch query degrades to an empty list.
- **Observability**: Identity Service call failures should be logged at Warning level through `ILogger`.

## Test strategy

- **Coverage target**: ≥ 80% line coverage of StudentsController's link/unlink/query-account-ID methods plus the IdentityAccountsController methods.
- **Error paths that must be tested**: empty IdentityAccountId, illegal GUID, Student HTTP BadRequest, Student HTTP NotFound, Identity Service HTTP failure, AppId/AppSecret not configured.
- **Test environment requirements**: `xUnit + Moq`; IStudentHttpClient and HttpClientFactory are replaced with Mocks.
- **Frontend verification**: the StudentView "Linked accounts" dialog's add/remove buttons are verified manually in the browser.
- **Prohibitions**: unit tests must not connect to real HTTP services or the Identity Service.

## Removed features

- **The teacher/assistant management page "Linked students" column and Manage button**: the original design provided a "Linked students" column on the teacher/assistant management page showing a student count, plus a "Manage" button opening `StudentAssociationDrawer`. That function duplicated the semantics of the student management page's "Linked accounts" dialog, and its count loading used N+1 queries (one HTTP request per teacher/assistant), a performance problem. The frontend and backend implementations were deleted on 2026-07-27:
  - Frontend: deleted the `StudentAssociationDrawer.vue` component, the `studentLinkApi.ts` service, and the "Linked students" column and related functions of TeacherView/AssistantView.
  - Backend: deleted `teacher_portal`/`assistant_portal`'s `GET/POST/DELETE /api/admin/{role}s/{userId}/students[/{studentId}]` endpoints and their unit tests.
  - Association management is done uniformly in the student management page's "Linked accounts" dialog, calling admin_portal's `GET/POST/DELETE /api/admin/students/{studentId}/accounts` endpoints, without distinguishing account roles.
