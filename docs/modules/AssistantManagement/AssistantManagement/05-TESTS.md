# Assistant Management — Test Specification

> The description below about forwarding the caller's Authorization is a historical design. The current proxy sends only the in-ticket server token and rejects inbound Authorization; the current rules are in the root AGENTS.md and docs/api.md.

## 1. AssistantPortalProxyMiddleware tests

Symmetric with the TeacherPortalProxyMiddleware tests:

| Scenario | Expected |
|------|------|
| Non-/api/assistant-portal request | goes straight to next, not proxied |
| /api/assistant-portal/admin/assistants | forwarded to {Address}/api/admin/assistants |
| /api/assistant-portal/auth/check-assistant | forwarded to {Address}/api/auth/check-assistant |
| Assistant Portal not configured | returns 503 |
| Assistant Portal unreachable | returns 502 |
| Authorization Bearer forwarded verbatim | the caller's Authorization header is forwarded verbatim; downstream checks role:admin |
| POST request body forwarded | the request body is forwarded correctly |

## 2. AssistantDbService tests

| Method | Scenario | Expected |
|------|------|------|
| AddAssistantByUserId | create | the assistant is created successfully, with subjects |
| GetByUserId | exists | returns the assistant entity, with the subject list |
| GetByUserId | missing | returns null |
| SetSubjectsByUserId | full set | the old subjects are cleared and the new ones written |
| AddSubjectByUserId | add | the subject association succeeds |
| AddSubjectByUserId | duplicate | idempotent, no error |
| RemoveSubjectByUserId | exists | removed successfully |
| RemoveSubjectByUserId | missing | returns null |
| RemoveAssistantByUserId | revoke | the assistant and their subjects are deleted |

## 3. AdminController endpoint tests

| Endpoint | Scenario | Expected |
|------|------|------|
| GET /assistants | normal | 200 + the assistant list |
| POST /identity-users/{userId}/grant-assistant | normal | 200 + success |
| POST /identity-users/{userId}/revoke-assistant | normal | 200 + success |
| GET /assistants/{userId}/subjects | normal | 200 + the subject list |
| PUT /assistants/{userId}/subjects | normal | 200 + success |
| POST /assistants/{userId}/subjects/{subject} | normal | 200 |
| DELETE /assistants/{userId}/subjects/{subject} | normal | 200 |
| GET /available-subjects | normal | 200 + subject options |

## 4. Removed tests

The following tests were deleted together with the feature (2026-07-27):

- `GetAssistantStudents_*` — deleted with the `GET /api/admin/assistants/{userId}/students` endpoint
- `AddAssistantStudent_*` — deleted with the `POST /api/admin/assistants/{userId}/students/{studentId}` endpoint
- `RemoveAssistantStudent_*` — deleted with the `DELETE /api/admin/assistants/{userId}/students/{studentId}` endpoint
