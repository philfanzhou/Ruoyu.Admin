# Assistant Management — Technical Design

> The description below about forwarding the caller's Authorization is a historical design. The current proxy sends only the in-ticket server token and rejects inbound Authorization; the current rules are in the root AGENTS.md and docs/api.md.

## 1. Admin Portal backend

### 1.1 AssistantPortalProxyMiddleware

- Location: `backend/Admin.WebApi/AssistantPortalProxyMiddleware.cs`
- Pattern: fully symmetric with TeacherPortalProxyMiddleware
- Registration: `app.UseMiddleware<AssistantPortalProxyMiddleware>()` in Program.cs
- Configuration: `AssistantPortalOptions` (Url)
- Authentication: forwards the caller's `Authorization: Bearer` verbatim; downstream checks `[Authorize(Roles="admin")]`; the static `X-Admin-Key` is no longer injected
- HttpClient: reuse or create `IHttpClientFactory("AssistantPortal")`

### 1.2 Configuration changes

- `appsettings.json`: add the `AssistantPortal` configuration section
- `Program.cs`:
  - `builder.Services.Configure<AssistantPortalOptions>(...)`
  - `builder.Services.AddHttpClient("AssistantPortal", ...)`
  - `app.UseMiddleware<AssistantPortalProxyMiddleware>()`
  - Log the AssistantPortal address at startup

## 2. Assistant Portal backend

### 2.1 AdminController endpoints

Retained endpoints:
- `GET /api/admin/assistants` — get the assistant list
- `POST /api/admin/identity-users/{userId}/grant-assistant` — grant assistant permission
- `POST /api/admin/identity-users/{userId}/revoke-assistant` — revoke assistant permission
- `GET/PUT/POST/DELETE /api/admin/assistants/{userId}/subjects[/{subject}]` — subject management
- `GET /api/admin/available-subjects` — available subject list

### 2.2 Database

`AssistantDbContext` contains the two tables `AssistantAccounts` and `AssistantSubjects`, with no student-association table.

## 3. Admin Portal frontend

### 3.1 API client

`frontend/src/services/assistantPortalApi.ts`

Interfaces symmetric with `teacherPortalApi.ts`:
- `getAssistants()`
- `grantAssistant(userId, { subjects })`
- `revokeAssistant(userId)`
- `getAssistantSubjects(userId)`
- `setAssistantSubjects(userId, subjects)`
- `addAssistantSubject(userId, subject)`
- `removeAssistantSubject(userId, subject)`
- `getAvailableSubjects()`

### 3.2 View component

`frontend/src/views/AssistantView.vue`

Layout symmetric with TeacherView.vue:
- Main table: avatar / User ID / phone number / username / remark / subjects / creation time / actions (subjects, revoke)
- Grant-permission dialog: search Identity users → choose subjects
- Subject-management dialog: batch-edit subjects

### 3.3 Routing

`router/index.ts` adds:
```ts
{
  path: '/assistants',
  name: 'assistants',
  component: () => import('../views/AssistantView.vue'),
  meta: { title: 'Assistant Management' }
}
```

The side navigation needs a matching entry.

## 4. Removed features

- **Linked-account management**: the original design provided a `PUT /api/admin/assistants/{userId}/user-id` endpoint to change the userId. Deleted on 2026-07-27.
- **Assistant-student association**: the original design provided `GET/POST/DELETE /api/admin/assistants/{userId}/students[/{studentId}]` endpoints and an `AssistantStudent` entity table. The frontend and backend implementations were deleted on 2026-07-27. Association management is done uniformly on the student management page.
