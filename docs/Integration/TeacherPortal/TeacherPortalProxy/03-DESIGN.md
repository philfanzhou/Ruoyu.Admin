# TeacherPortalProxy Data Models

> The description below about forwarding the caller's Authorization is a historical design. The current proxy sends only the in-ticket server token and rejects inbound Authorization; the current rules are in the root AGENTS.md and docs/api.md.

## Configuration data

### TeacherPortalOptions

The configuration options the middleware needs, obtained through dependency injection.

| Property | Type | Description |
|------|------|------|
| Url | string | the TeacherPortal's base address (for example `http://teacher-portal:8080`) |

## Request headers

The middleware forwards all of the caller's request headers verbatim (excluding `Content-*` and `Host`). The `Authorization: Bearer` header is forwarded to the Teacher Portal as-is, and the Teacher Portal validates the JWT through `[Authorize(Roles="admin")]`. The static `X-Admin-Key` is no longer injected.

## Response status codes

| Status code | Scenario |
|--------|------|
| 2xx | forwarded successfully and a normal teacher-portal response was received |
| 502 | the TeacherPortal is unreachable |
| 503 | TeacherPortalOptions is not configured |

## No persisted data

TeacherPortalProxyMiddleware itself involves no database operations; all data is handled by the downstream TeacherPortal.
