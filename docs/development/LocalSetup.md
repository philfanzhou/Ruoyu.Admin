# Local environment setup (LocalSetup)

## Prerequisites

| Dependency | Version | Purpose |
|------|------|------|
| .NET SDK | 10.0+ | Backend build and run |
| Node.js | 18+ | Frontend build (optional) |
| PostgreSQL | 12+ | Audit database |

## Backend configuration

1. Copy the configuration files:
   ```bash
   cd backend
   # In the Development environment, appsettings.Development.json already contains the development defaults and can be used directly
   ```

2. Key configuration items (development defaults live in `appsettings.Development.json`; the base `appsettings.json` no longer carries environment-specific values):

| Configuration key | Default | Description |
|--------|--------|------|
| `ConnectionStrings:AuditDb` | `Host=localhost;Port=5432;Database=ruoyu_admin;Username=phil` | Audit database connection string (local development default; production is overridden by Consul) |
| `Database:Name` | `ruoyu_admin` | Database name (used when synthesizing the connection string with Consul `PostgreSql:*`) |
| `StudentService:Url` | `http://localhost:5005` | Student HTTP service |
| `MistakeService:Url` | `http://localhost:5007` | Mistake HTTP service |
| `MistakeService:UseSessionToken` | `false` | Mistake outbound session opt-in only; enabling requires an explicit root origin (path `/`), http and https accepted equally, public deployments should use HTTPS (deployment advice, not enforced in code) |
| `IdentityService:Authority` | (none; Development defaults to `http://127.0.0.1:5002`, see `appsettings.Development.json`) | Identity hosted OIDC discovery + server-session HTTP proxy. `appsettings.json` has no `IdentityService` section; locally you must inject `AppId`, `AppSecret`, etc. via environment variables / user-secrets |
| `TeacherPortal:Url` | `http://localhost:5004` | Teacher Portal HTTP service |
| `AssistantPortal:Url` | `http://localhost:5021` | Assistant Portal HTTP service |

### Hosted login development configuration

The current version runs only the server-session hosted login. You must configure `IdentityService:Authority/AppId/AppSecret`, `AdminOidc:RedirectUri/PostLogoutRedirectUri`, the current administrator allowlist and the ADMIN application audience, and complete real downstream trust plus exact Code/Logout registration. The five legacy keys `AdminOidc:Enabled/UseSessionForAdminApi/UseSessionForLogout/UseSessionForIdentityProxy/UseSessionForPortalProxies` no longer select the run mode: remove them; only omission or the canonical lowercase `true` passes migration validation, and an explicit `false`, empty, or malformed value is rejected at startup in every environment. The password entry point is permanently `410 legacy_login_disabled`; API/images/the three proxies/association queries always use the same administrator session and CSRF boundary; any inbound Authorization gets a fixed 401; there is no capability switch and no browser-JWT fallback mode.

The development SPA uses `http://127.0.0.1:8090`, with the full `/api/*` proxied by Vite to the backend's hardcoded 5020. The registration and the BFF configuration must use the browser origin's `http://127.0.0.1:8090/api/auth/oidc/callback` and `/api/auth/oidc/logout/return` — never register the internal 5020, localhost, or wildcards. Integrated mode switches to its actual same-origin address. Transport security is a deployment decision: at the configuration level http and https are accepted equally (structural rules unchanged, `localhost` host names stay rejected); public deployments should use HTTPS with a normal CA (a recommendation, not enforced); SignaCore Host 0.1.16 likewise has no HTTP origin gate, and earlier Host versions must admit this service's HTTP redirect origin under their own Testing gate (a deployment-side fact). Reuse the Confidential, PerApplication ADMIN audience, Code + openid profile + S256 client of `IdentityService:Authority/AppId/AppSecret`, without enabling refresh; the Secret is injected only via environment variables or user-secrets.

Cookies hold only an opaque reference; tickets have an 8-hour absolute lifetime, token expiry requires explicit re-login, and restarts lose them. Unsafe requests take the ordinary CSRF via the shared client's single flight; logout uses the separate logout CSRF, so even an expired token or removal from the allowlist still lets the user log out. The three proxies and images all depend on the current server authorization. For full TLS, allowlist, logging, single-instance limits, and rollback conditions, see [Deployment.md](./Deployment.md#signacore-hosted-login-the-only-mode).

The automated full handshake uses the test RSA, Discovery/JWKS/token stand-ins, and Testcontainers PostgreSQL of `backend/Tests/Integration/OidcTestAuthority.cs`, `dotnet test Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~Oidc"`; it never connects to production Identity.

The Mistake outbound suite runs `dotnet test Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~MistakeSession"`, covering the 14 real HTTP methods, a real Program Code/PKCE current server ticket, same-request ticket rotation/revocation, concurrency, redirect/Cookie isolation, and cancellation. When enabling, first run the actual `dotnet Admin.WebApi.dll --validate-auth-config`; you must not make localhost an exception, skip TLS, or pass the AppSecret off as the user. `false` keeps the old anonymous downstream compatibility, and management inbound still accepts Session only. Real official Provider/Mistake/Student/S3 joint verification is performed separately per [Deployment](./Deployment.md#mistake-outbound-session-ikjnxa).

### Database connection strategy

Admin Portal assembles its connection string through `SharedPostgreSqlConnectionStringFactory.BuildOrFallback`, consistent with services like mistake / student:

1. At startup, call `AddRuoyuConsulConfiguration` to load the shared configuration (the Consul address comes from `CONSUL_*` environment variables or `RuoyuConsulOptions` defaults; the base `appsettings.json` no longer contains a `Consul` section)
2. Obtain `PostgreSql:Host/Port/Username/Password` from the Consul KV `config/ruoyu/shared.json`
3. When `PostgreSql:Host`, `PostgreSql:Username`, and `Database:Name` are all non-empty, synthesize a PostgreSQL connection string with the local `Database:Name`
4. Otherwise fall back to the local `ConnectionStrings:AuditDb` (provided only as a Development default)

- Local development: `ConnectionStrings:AuditDb` points at the local PostgreSQL (`Host=localhost;Username=phil`, no password); it runs without Consul
- Production: delivered by Consul's `PostgreSql:*`; the base `appsettings.json` no longer carries a local connection-string fallback

3. Environment variable overrides (optional):
   ```bash
   export USE_LOCAL_OSS=1           # use the local file system instead of S3
   export OSS_LOCAL_PATH=data/oss   # local OSS storage path
   ```

4. IOssService credential permission notes (Phase 4 change):

   Admin Portal keeps `IOssService` for audit scenarios, but the credential permissions are downgraded to read-only + limited write:

   | Operation | Permission | Purpose |
   |------|------|------|
   | `ListObjects` | read-only | audit browsing |
   | `GetPresignedUrl` | read-only | generating presigned URLs |
   | `ObjectExists` | read-only | audit verification |
   | `Download` | read-only | image viewing (switched to presigned URLs after the rework; can be removed later) |
   | `Delete` | write | zombie file cleanup |
   | `CopyObject` | write | migration assistance |

   > Local development with `LocalFileOssService` (`USE_LOCAL_OSS=1`) has no permission restrictions; production must configure OSS credentials with matching permissions.

5. OSS address configuration:

   | Configuration key | Description | Example |
   |--------|------|------|
   | `Oss:InternalEndpoint` | the S3 address the Admin backend actually connects to | `localhost:8333` |
   | `Oss:InternalSecure` | whether the internal connection uses HTTPS | `false` |
   | `Oss:PublicBaseUrl` | the public presigned base URL used by browsers | `https://oss.example.com/oss` |

   These S3 addresses are not read when local development uses `LocalFileOssService`. The Admin frontend Nginx provides no `/oss/` proxy; public objects are uniformly proxied by the User Web Nginx.

## Frontend configuration

```bash
cd frontend
npm install
```

The frontend dev server listens on 8090 by default; hosted login accesses `http://127.0.0.1:8090`, and API requests are proxied to backend port 5020.

## Downstream service dependencies

Admin Portal depends on the following downstream services running:

| Service | Port | Required | Impact when unavailable |
|------|------|--------|-------------|
| Student Service | 5005 | required | Student management, upload records, image presigned URLs, and image migration all unavailable; audit path aggregation unavailable |
| Mistake Service | 5007 | required | Mistake management and image presigned URLs unavailable; audit reference aggregation aborts the whole run per the invariant (connection refusals and non-JSON failures both propagate upward, see [#28](https://github.com/philfanzhou/Ruoyu.Admin/issues/28)) |
| Identity Service | 5002 | optional | identity proxy returns 502; account batch lookup returns an empty list |
| Teacher Portal | 5004 | optional | teacher portal proxy returns 502/503 |
| Assistant Portal | 5021 | optional | assistant portal proxy returns 502/503 |
| OSS storage | - | required (audit scenarios) | audit browsing and zombie cleanup unavailable; image viewing goes through downstream HTTP services for presigned URLs |

> **Phase 4 change**: when the Student Service is unavailable, the impact widens (image presigned URLs, image migration, and path aggregation added). When the Mistake Service is unavailable, the audit aborts the whole run per the invariant instead of continuing (the historical "skip aggregation" wording conflicts with the current implementation's invariant; connection refusals and non-JSON failures both propagate upward, see [#28](https://github.com/philfanzhou/Ruoyu.Admin/issues/28)). OSS storage affects only audit scenarios; image viewing now goes through downstream HTTP services for presigned URLs and Admin no longer reads object contents directly.
>
> When you need a runnable local full stack (including Identity and the three reference-source stand-ins), see [LocalRegressionEnv.md](./LocalRegressionEnv.md).

> For detailed configuration notes see [Deployment.md](./Deployment.md)

### Prepared logout (#46; owned by the SignaCore package since #83)

Logout always uses prepared logout, with the package-owned endpoint `POST /api/auth/oidc/logout`. The required `AdminOidc:PostLogoutRedirectUri` is same-origin with the RedirectUri, with the fixed path `/api/auth/oidc/logout/return` and no query/fragment/userinfo; the SignaCore application registration's PostLogout callback must be changed to this value by the maintainer. http and https are accepted equally (structural rules unchanged); public deployments should use HTTPS (a recommendation, not enforced). Non-POST logout is a fixed 405, and every POST attempt clears the old `adminAuthToken` Cookie.

The SPA first GETs `/api/auth/oidc/csrf` (publicly issuing `{"token":"..."}` and rotating the shared `adminCsrf` Cookie), then submits `__RequestVerificationToken` via a navigational form POST (or a single-value `X-CSRF-TOKEN` header). A missing/wrong token is a fixed `400 {"outcome":"csrf_rejected"}` without touching the session; logout is never blocked by access-token expiry or the administrator allowlist. Inside a gate serialized per session key, the package first removes the ticket and deletes the session Cookie, then sends HTTP Basic authentication + the server id_token_hint + the PostLogout URI + a one-time state to the Authority's `/oauth2/logout/requests`, without retrying or following redirects; success is a 302 to the validated same-origin logout_uri, failure/timeout/cancellation is a fixed `200 {"outcome":"local_only"}`. The return trip `/api/auth/oidc/logout/return?state=...` consumes atomically once with the binding Cookie (5 minutes); the correct case is a 302 to `/login`, everything else a fixed 400 HTML.


The success JSON `{success:true,upstreamLogout:true,logoutUrl:...}` carries only the unique URL that is same-origin or relative `/oauth2/logout?logout_handle=<43base64url>` after navigational validation; a relative URL resolves to an absolute URL same-origin with the Authority. Preparation failure, malformed/oversized response, wrong URL, timeout, cancellation, insufficient capacity, or a missing ID token all permanently keep the local logout — when the response is still writable, `{success:true,upstreamLogout:false}`; on disconnection the browser is not guaranteed to receive the result. Recovery re-queries the local state; remote success is never presumed. The overall send/read has a 10-second cap and the JSON at most 4096B; the private HTTP carries no browser Cookie/Authorization, logs, or redirects, and the protocol paths enter no telemetry.


### Token expiry and explicit reauthentication (#45)

In the only-session mode, when a valid administrator ticket hits a strict `expires_at` deadline, the business gate returns `401 {"error":"reauthentication_required"}`, with zero downstream/OSS deletion, no clearing of the server ID token, and no refresh or write replay. `GET /api/auth/session` returns `{authenticated,displayName,requiresReauthentication}`: valid token true/false; only a valid token at expiry false/true (display name kept); anonymous/revoked/8h-expired/missing token/bad deadline false/false with null display name. Non-admin 403 takes precedence; any Authorization 401. The browser only explicitly enters the controlled authorization at `/api/auth/oidc/start?returnUrl=...`; when the upstream session can be reused it returns immediately, otherwise the hosted login shows; cancel/failure keeps the existing fixed results, and the BFF never auto-challenges. Fake tests verify both return timings and before/exactly-at/after the deadline/8h plus concurrency, and cannot prove the production SignaCore timings; the real joint debugging still belongs to #41/IKJ8MO.

### Portal server-side credentials (#44)

The full Teacher/Assistant proxy prefixes share the administrator/CSRF gate before authentication/authorization; a valid access token at expiry returns `401 reauthentication_required` with zero outbound, only the server Bearer is sent, and browser Cookie, Host, CSRF, and gateway headers plus downstream Set-Cookie are stripped. The admin/auth/other path mappings, query/body, 503/502, and downstream 401/403 are preserved; cancellation propagates without replay. Association queries share the same administrator boundary, forward only the server token, and reject before the Student/portal calls on failure. A single-sided business failure or malformed response keeps that side an empty list while the other side is normal; this aggregation guarantees neither failure visibility nor cross-portal consistency. Production rollout is not in #75's implementation scope; formal verification of the current combination uses the official Provider and real downstream. The dedicated `PortalSession_` and `AssociationsSession_` suites run on an isolated database/fake HTTP and never touch production.

### Identity proxy server-side session (#43)

The full `/api/identity` prefix (case, root, and trailing slash) passes the shared administrator and CSRF boundary before authentication/authorization. Any inbound Authorization is rejected; a valid access token at expiry returns `401 reauthentication_required` with zero outbound; only the valid in-ticket server access token is forwarded, and after stripping browser Cookie, Host, X-CSRF-TOKEN, and forged gateway headers, this service's own AppId/AppSecret is injected. Downstream Set-Cookie does not reach the browser; 401/403/structured 503 and Retry-After pass through as-is, network failure gives 502, and cancellation propagates without replay. AppSecret stripping always remains, and the three proxies share the same session boundary. Production platform registration is not performed by this item; the formal combination gate belongs to #75. The dedicated `FullyQualifiedName~IdentitySession_` tests use fake HTTP and an isolated database, with no production OSS deletion.
