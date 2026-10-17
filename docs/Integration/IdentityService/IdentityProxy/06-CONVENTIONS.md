# IdentityProxy Conventions and Patterns

## Middleware patterns

### Request interception and forwarding

IdentityProxyMiddleware uses the reverse-proxy pattern:
- Intercepts requests with a specific path prefix
- Rewrites the path and forwards to the downstream service
- Injects authentication automatically
- Forwards the response to the client verbatim

### Path rewriting conventions

- Prefix replacement: `/api/identity` → `/api`
- The rest of the original path is preserved
- Query parameters and the request body are preserved

### Authentication header conventions

Application-level authentication is passed through custom headers:
- `X-Admin-AppId`: identifies the admin console application
- `X-Admin-AppSecret`: the application secret

The `X-Admin-` prefix marks this as admin-console-level authentication [inferred].

## Error handling conventions

| Scenario | HTTP status code | Description |
|------|-----------|------|
| Downstream service unreachable | 502 Bad Gateway | the standard gateway error |

## Configuration conventions

### The Options pattern

Uses ASP.NET Core's Options pattern:
- `IdentityServiceOptions` is obtained through dependency injection
- Configuration items load from appsettings.json or environment variables

### Configuration structure

```json
{
  "IdentityService": {
    "Address": "http://identity-service:8080",
    "AppId": "admin-portal",
    "AppSecret": "***"
  }
}
```

## Security conventions

- AppSecret is sensitive configuration and must not be logged
- The proxy middleware should not parse or log request-body contents [inferred]
- All communication to the IdentityService should use HTTPS [inferred]
