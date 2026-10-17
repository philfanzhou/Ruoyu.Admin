# TeacherPortalProxy Conventions and Patterns

## Middleware patterns

### Request interception and forwarding

TeacherPortalProxyMiddleware uses the reverse-proxy pattern, consistent with IdentityProxyMiddleware:
- Intercepts requests with a specific path prefix
- Rewrites the path and forwards to the downstream service
- Injects authentication automatically
- Forwards the response to the client verbatim

### Path rewriting conventions

Unlike IdentityProxyMiddleware's simple prefix replacement, TeacherPortalProxyMiddleware uses multi-rule path rewriting:

| Rule | Original prefix | Target prefix | Description |
|------|---------|---------|------|
| admin | `/api/teacher-portal/admin` | `/api/admin` | admin interface |
| auth | `/api/teacher-portal/auth` | `/api/auth` | authentication interface |
| default | `/api/teacher-portal/other` | `/api/admin` | default routing |

**Design intent**: the teacher portal's interfaces fall into two classes, admin and authentication; non-authentication paths are uniformly classified as admin paths [inferred].

### Authentication header conventions

Authentication is passed through a single custom header:
- `X-Admin-Key`: the admin console's API key

Unlike IdentityProxy's two-header authentication (AppId + AppSecret), the TeacherPortal uses a single-key authentication pattern.

## Error handling conventions

| Scenario | HTTP status code | Description |
|------|-----------|------|
| Configuration not ready | 503 Service Unavailable | the service is temporarily unavailable |
| Downstream service unreachable | 502 Bad Gateway | gateway error |

**Difference from IdentityProxy**: TeacherPortal adds the 503 status code for the missing-configuration case, signaling a recoverable unavailability (usable once configured).

## Configuration conventions

### The Options pattern

Uses ASP.NET Core's Options pattern:
- `TeacherPortalOptions` is obtained through dependency injection
- Configuration items load from appsettings.json or environment variables

### Configuration structure

```json
{
  "TeacherPortal": {
    "Address": "http://teacher-portal:8080",
    "AdminApiKey": "***"
  }
}
```

## Security conventions

- AdminApiKey is sensitive configuration and must not be logged
- The proxy middleware should not parse or log request-body contents [inferred]
- All communication to the TeacherPortal should use HTTPS [inferred]

## Proxy middleware comparison

| Property | IdentityProxy | TeacherPortalProxy |
|------|--------------|-------------------|
| Intercepted paths | `/api/identity/*` | `/api/teacher-portal/*` |
| Path rewriting | single prefix replacement | multi-rule + default routing |
| Authentication | AppId + AppSecret (two headers) | AdminApiKey (single header) |
| Unconfigured response | [to confirm] | 503 |
| Unreachable response | 502 | 502 |
