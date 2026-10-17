# Identity Account Linking (AccountLinking)

## Feature name and one-line summary

**AccountLinking** — administrators link identity accounts to student profiles, unlink them, and query in both directions.

## Core user stories

As an administrator, I want to link identity accounts to student profiles with association management and reverse lookup, so that students can sign in to the system with different identities and administrators can trace the associations. Association management is done uniformly in the "Linked accounts" dialog on the student management page, without distinguishing account roles (teachers/assistants/regular users are all allowed); the teacher/assistant management pages no longer offer a link-students function.

## Supplementary constraints

1. **Idempotency**: linking is not idempotent (uniqueness of linking the same account to the same student is guaranteed by the Student HTTP service layer)
2. **Concurrency**: link uniqueness is guaranteed by the Student HTTP service layer; the Admin Portal adds no extra concurrency control
3. **Transaction boundary**: link/unlink is a single HTTP call with no cross-service transaction
4. **Failure degradation**: when the Identity Service is unavailable, the batch query of identity account information returns an empty list instead of an error
5. **Link validation**: IdentityAccountId must be a legal GUID format and must not be empty
6. **Unlink parameters**: studentId and accountId must both be provided, both as GUID route parameters
7. **Single entry point**: creating and deleting associations happens only from the "Linked accounts" dialog on the student management page, calling admin_portal's `GET/POST/DELETE /api/admin/students/{studentId}/accounts` endpoints; any Identity account can be linked (teachers, assistants, and regular users alike), with no distinction by role

## Key acceptance conditions summary

1. The list of identity account IDs already linked to a student can be viewed
2. A new identity account can be linked to a student profile; IdentityAccountId must be a legal GUID
3. The same identity account cannot be linked twice to the same student
4. An identity account can be unlinked from a student profile
5. The batch query of identity account information goes through the Identity Service HTTP API
6. When the Identity Service is unavailable, the batch query returns an empty list
7. The list of students linked to an identity account ID can be looked up in reverse
8. The "Linked accounts" column on the student management page provides a "Manage" button; clicking it opens a dialog showing all Identity accounts linked to that student (regardless of role) and supports searching any Identity user to add/remove (calling `GET /api/admin/students/{studentId}/accounts`, `POST /api/admin/students/{studentId}/accounts`, `DELETE /api/admin/students/{studentId}/accounts/{accountId}`)

## Removed features

- **The student-list "Linked accounts" (Identity binding status) column**: the original design showed `identityAccountIds[0]` (the first bound Identity account) in a dedicated student-list column plus a "Linked accounts" button opening a single-select linking dialog. That column duplicated the semantics of the "Linked teachers/assistants" column and was merged on 2026-07-27: the student list keeps only one "Linked accounts" column (the renamed former "Linked teachers/assistants" column), and the dialog opened by the "Manage" button uniformly manages all of that student's Identity account associations.
- **The student-list "Linked teachers/assistants" count badge**: the original design showed an "X people/none" count badge in the student list; because the data was lazily loaded after clicking "Manage", every row initially displayed "none", which was misleading. On 2026-07-27 the count badge was removed, keeping only the "Manage" button; the count is shown inside the dialog opened on click.
- **The teacher/assistant management page "Linked accounts" column**: the original design offered "link/change Identity account" on the teacher/assistant management page (`PUT /api/{teacher-portal|assistant-portal}/admin/{role}s/{userId}/user-id`). That function was never requested and introduced extra userId-change complexity; its frontend and backend implementations were deleted on 2026-07-27. A teacher/assistant's `userId` is bound at `grant-teacher` / `grant-assistant` grant time and can no longer be changed afterwards.
- **The teacher/assistant management page "Linked students" column and Manage button**: the original design provided a "Linked students" column on the teacher/assistant management page showing a student count, plus a "Manage" button opening `StudentAssociationDrawer`. That function duplicated the semantics of the student management page's "Linked accounts" dialog, and its count loading used N+1 queries (one HTTP request per teacher/assistant), a performance problem. The frontend and backend implementations were deleted on 2026-07-27:
  - Frontend: deleted the `StudentAssociationDrawer.vue` component, the `studentLinkApi.ts` service, and the "Linked students" column and related functions of TeacherView/AssistantView.
  - Backend: deleted `teacher_portal`/`assistant_portal`'s `GET/POST/DELETE /api/admin/{role}s/{userId}/students[/{studentId}]` endpoints and their unit tests.
  - Association management is done uniformly in the student management page's "Linked accounts" dialog.

## Out of scope

- Creating, updating, and deleting identity accounts
- Identity account password management
- The authentication flow (login/logout)
- Cross-tenant identity account linking

## Document index

- [02-SPEC.md](./02-SPEC.md) — detailed requirements and acceptance criteria list
- [03-DESIGN.md](./03-DESIGN.md) — architecture design, interface signatures, and data flow
- [04-TASKS.md](./04-TASKS.md) — development/verification task list
- [05-TESTS.md](./05-TESTS.md) — unit and integration test plan
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — naming conventions, log safety, and code style
