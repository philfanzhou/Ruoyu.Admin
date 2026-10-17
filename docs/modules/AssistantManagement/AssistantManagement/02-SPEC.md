# Assistant Management — Functional Specification

> The description below about forwarding the caller's Authorization is a historical design. The current proxy sends only the in-ticket server token and rejects inbound Authorization; the current rules are in the root AGENTS.md and docs/api.md.

## 1. Assistant permission management

### 1.1 Assistant list

- API: `GET /api/assistant-portal/admin/assistants`
- Returns all assistant accounts, including userId, phone, username, subjects, isActive, createdAt, updatedAt
- Table columns: User ID, phone number, username, remark, subjects (Tag list), creation time, actions

### 1.2 Grant assistant permission

- API: `POST /api/assistant-portal/admin/identity-users/{userId}/grant-assistant`
- Request body: `{ subjects: number[] }`
- Interaction: a dialog searches Identity users → select → choose subjects → confirm the grant

### 1.3 Revoke assistant permission

- API: `POST /api/assistant-portal/admin/identity-users/{userId}/revoke-assistant`
- Interaction: a row-level "Revoke permission" button with a confirmation step

### 1.4 Subject management

- Query: `GET /api/assistant-portal/admin/assistants/{userId}/subjects`
- Full set: `PUT /api/assistant-portal/admin/assistants/{userId}/subjects`
- Add: `POST /api/assistant-portal/admin/assistants/{userId}/subjects/{subject}`
- Remove: `DELETE /api/assistant-portal/admin/assistants/{userId}/subjects/{subject}`
- Available subjects: `GET /api/assistant-portal/admin/available-subjects`
- Interaction: a closable Tag removes a single subject, plus a "Manage" button opening a dialog for batch editing

## 2. Admin Portal proxy

### 2.1 AssistantPortalProxyMiddleware

Path mapping rules (symmetric with TeacherPortalProxyMiddleware):

| Admin request path | Forwarded to the Assistant Portal |
|---------------|------------------------|
| `/api/assistant-portal/admin/*` | `/api/admin/*` |
| `/api/assistant-portal/auth/*` | `/api/auth/*` |

Authentication: forwards the caller's `Authorization: Bearer` verbatim; downstream checks `[Authorize(Roles="admin")]`; the static `X-Admin-Key` is no longer used

### 2.2 Configuration

```json
{
  "AssistantPortal": {
    "Address": "http://ruoyu-assistant-api:5021"
  }
}
```

### 2.3 Environment variables

- `AssistantPortal__Url`

## 3. Removed features

- **Linked-account management (PUT /api/assistant-portal/admin/assistants/{userId}/user-id)**: the original design offered "link/change Identity account" on the assistant management page. That function was never requested and introduced extra userId-change complexity; its frontend and backend implementations were deleted on 2026-07-27. An assistant's `userId` is bound at `grant-assistant` grant time and can no longer be changed afterwards.
- **Assistant-student association (GET/POST/DELETE /api/admin/assistants/{userId}/students[/{studentId}])**: the original design provided a "Linked students" column on the assistant management page showing a student count plus a "Manage" button. That function duplicated the semantics of the student management page's "Linked accounts" dialog, and its count loading used N+1 queries, a performance problem; the frontend and backend implementations were deleted on 2026-07-27. Association management is done uniformly in the "Linked accounts" dialog on the student management page; see [StudentManagement/AccountLinking](../../StudentManagement/AccountLinking/01-FEATURE.md).
