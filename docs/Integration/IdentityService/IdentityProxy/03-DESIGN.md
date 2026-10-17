# IdentityProxy Data Models

## Configuration data

### IdentityServiceOptions

The configuration options the middleware needs, obtained through dependency injection.

| Property | Type | Description |
|------|------|------|
| Address | string | the IdentityService's base address (for example `http://identity-service:8080`) |
| AppId | string | the application ID the admin console registered with the identity service |
| AppSecret | string | the application secret the admin console registered with the identity service |

## Request headers

The middleware injects the following headers when forwarding requests:

| Header | Source | Description |
|--------|------|------|
| X-Admin-AppId | IdentityServiceOptions.AppId | identifies the admin console application |
| X-Admin-AppSecret | IdentityServiceOptions.AppSecret | the admin console application's authentication secret |

## Response status codes

| Status code | Scenario |
|--------|------|
| 2xx | forwarded successfully and a normal identity-service response was received |
| 502 | the IdentityService is unreachable |

## No persisted data

IdentityProxyMiddleware itself involves no database operations; all data is handled by the downstream IdentityService. IdentityAccountsController's data models [to confirm].
