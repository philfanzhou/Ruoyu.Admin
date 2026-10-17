# Local non-production regression environment (LocalRegressionEnv)

> The first half keeps the #24 historical old-complete-version on-site recipe; its password/Bearer login steps do not apply to the current main. The current version must combine the #41 hosted SPA — the password entry point is permanently 410, invalid authentication configuration refuses startup, and the API rejects any Authorization; for the latest controlled deployment configuration see [Deployment.md](./Deployment.md#signacore-hosted-login-the-only-mode). The current version's authentication cannot be restored by re-enabling an old path.

Managed assignment needs real Student/Mistake, PG/S3, and working TLS/registered identities; the old downstream stand-ins in this document do not simulate intake idempotency, leases, or a lost response after commit, and cannot replace that acceptance. For configuration and real verification entry points see the [managed integration contract](../Integration/ManagedAssignment.md); keep the capability off by default.

This document provides a complete recipe for going **from a clean clone on a dev machine to a runnable [#24](https://github.com/philfanzhou/Ruoyu.Admin/issues/24) real-environment regression**: local PostgreSQL + SignaCore local Identity + local stand-ins for the three downstream reference sources + `USE_LOCAL_OSS=1` local object directory. The recipe keeps the old complete version's login, old downstream DTOs, and local object directory regression; the real three-party acceptance of the current StorageReferences v1 must use a production Host, PG, normal TLS, and the dedicated RS256, which these stand-ins cannot replace. All audit resolves refuse deletion.

> **Security boundary (must not be relaxed)**
> - Any `DeleteAsync` triggered by the fixtures may target only `OSS_LOCAL_PATH` (the local directory). The seed script refuses to run unless `USE_LOCAL_OSS` is set to `1`.
> - Any credentials (Identity `AppId`/`AppSecret`, database passwords) go through environment variables or user-secrets, never into version control.
> - The stand-ins and seed scripts hold and forward no real downstream credentials.
> - **Deletion regression must never point at a production bucket.**

## Process and port overview

| Process | Port | Notes |
|------|------|------|
| Admin.WebApi (this repository) | 5020 (hardcoded) | the admin BFF under regression; the SPA production mode is served by nginx inside the container — locally use the frontend dev server or copy `wwwroot` |
| SignaCore Identity (local instance) | 5002 | a required part of the login chain, not a stand-in (see below) |
| Local stand-ins (single process) | 5005 / 5007 / 5009 | the three read-only reference queries Student / Mistake / Homework |
| PostgreSQL (local instance) | 5432 | only the two audit tables of `ruoyu_admin` |

Teacher Portal (5004) and Assistant Portal (5021) are not in this recipe: #24 verifies only their "proxy 502/503" degraded behavior for those two pages, which is exactly the behavior when unconfigured; wire them in per their own service documentation if positive cases are needed.

## Step 0: prerequisites

- .NET SDK 10 (same as CI's `10.0.x`)
- Local PostgreSQL 12+ (`initdb` + `postgres` running, or a single docker container)
- Optional: Node 20 + `npm ci` in `frontend/` (if you want the UI via the frontend dev server)

## Step 1: PostgreSQL

1. Start a local PostgreSQL (example: `docker run -d --name ruoyu-admin-pg -p 5432:5432 -e POSTGRES_PASSWORD=ruoyu-dev postgres:16`).
2. Either create the database `ruoyu_admin` by hand first, or explicitly set `Database__AllowCreate=true` locally to let the shared startup gate create the missing database (default false, refusal code `database_target_preparation.creation_not_allowed`). Table creation is performed by the shared `StartupDatabaseGate` invoking `AuditMigrationExecutor` to run the EF Core baseline migration under a real advisory lock; the seed script writes fixtures only after the startup gate completes.
3. The default connection string in `appsettings.json` hardcodes `Username=phil` and **must** be overridden per machine via environment variables (Admin.WebApi and the seed script read the same key):

```bash
export ConnectionStrings__AuditDb="Host=localhost;Port=5432;Database=ruoyu_admin;Username=ruoyu-dev;Password=ruoyu-dev"
```

## Step 2: SignaCore Identity local instance (a required part of login regression)

The first item of #24 is login/logout, and all `/api/*` is under FallbackPolicy protection, so Identity is not optional. Follow `docs/development/LocalSetup.md` and `FirstRunSetup.md` in the [SignaCore](https://github.com/philfanzhou/SignaCore) repository to start its local instance (listening on port 5002, reachable at both `localhost` and `127.0.0.1`), and:

1. Configure the first-run Setup Mode's public base URL / issuer as `http://127.0.0.1:5002` (SignaCore stores it in its database, see its `docs/development/LocalSetup.md`). Its client package requires the discovery issuer to be exactly equal to this service's configured `IdentityService:Authority` (this repository's `AdminOidcTests` uses Authority==Issuer==discovery issuer, with issuer mismatch as a failure case), and neither side may use the `localhost` host name — this repository's `IsSafeUri` structurally rejects `localhost` host names (see "FAQ"). The listen address does not change; only the external base URL / issuer does.
2. Register an `admin_portal` application on the Identity side and obtain the local `AppId` / `AppSecret`.
3. Create a local test user and note their Identity **user id** (GUID).
4. Add that user id to the `AdminPortal:AdminUserIds` allowlist — `AdminAuthController.Callback` returns `role:admin` only for users on the allowlist; users off the list can log in but get no admin role.

Admin.WebApi's two startup hard gates must both be satisfied:

- Missing `IdentityService:AppId` / `AppSecret` → throws `InvalidOperationException` at startup (`Program.cs`).
- Authority or Issuer using `http://` with `RequireHttpsMetadata=true` → the old `IdentityAuthenticationOptionsValidator` check was deleted with #103; the current hard gates are the structural validations of `AdminOidcSettings.Read` / `MistakeSessionSettings.Read` (`http://` and `https://` accepted equally, `localhost` host names structurally rejected), plus `ManagedAssignmentOptions` requiring `IdentityService:RequireHttpsMetadata=false` when `Mistake:ManagedAssignmentEnabled=true`. Locally, still always set `IdentityService:RequireHttpsMetadata=false`.

Local injection (choose one; neither enters version control): environment variables

```bash
export IdentityService__AppId="<local AppId>"
export IdentityService__AppSecret="<local AppSecret>"
export IdentityService__Authority="http://127.0.0.1:5002"
export IdentityService__Issuer="http://127.0.0.1:5002"
export IdentityService__Audience="PlatformAudience"   # must match the aud of SignaCore-issued JWTs
export IdentityService__RequireHttpsMetadata="false"
export AdminOidc__RedirectUri="http://127.0.0.1:8090/api/auth/oidc/callback"
export AdminOidc__PostLogoutRedirectUri="http://127.0.0.1:8090/api/auth/oidc/logout/return"
export AdminPortal__AdminUserIds='["<Identity user id of the local test user>"]'
```

`AdminOidc__RedirectUri` / `AdminOidc__PostLogoutRedirectUri` are required keys: `appsettings.json`'s `AdminOidc` is an empty section and `appsettings.Development.json` has none, so a missing value throws `AdminOidc:RedirectUri` at startup. The two must be same-origin with the fixed paths `/api/auth/oidc/callback` and `/api/auth/oidc/logout/return`. Pick the value per the browser's actual access origin, exactly matching the SignaCore-side registration: the frontend dev server (step 3) is `http://127.0.0.1:8090/...`, consistent with the existing position in [LocalSetup.md](./LocalSetup.md#hosted-login-development-configuration) that "the internal 5020, localhost, or wildcards must not be registered"; integrated mode (build output copied into `wwwroot/`) switches to its actual same-origin address (e.g. `http://127.0.0.1:5020/api/auth/oidc/callback`).

Or user-secrets (under `backend/Admin.WebApi/`, `dotnet user-secrets set "IdentityService:AppId" ...`). Note that `appsettings.json` has **no** `IdentityService` section; `appsettings.Development.json` already carries local defaults for Authority/Issuer/Audience/RequireHttpsMetadata (`http://127.0.0.1:5002`), so starting in the Development environment only needs AppId/AppSecret plus `AdminOidc:RedirectUri` / `AdminOidc:PostLogoutRedirectUri`.

## Step 3: start Admin.WebApi (local OSS mode)

```bash
cd backend
export USE_LOCAL_OSS=1
export OSS_LOCAL_PATH=data/oss        # this is the default; resolved relative to the CWD (backend/)
dotnet run --project Admin.WebApi/Admin.WebApi.csproj
```

The first startup runs the EF Core baseline migration through the shared startup gate to create the tables; only after success does it listen and start the worker (the seed script requires the tables to exist). An existing database only gets observed, needs no CREATEDB; unknown structures are refused for takeover. Readiness uses the shared singleton receipt and the scoped EF Core read-only mapped probe; not-started/running/failed receipts mean zero database access, and the normal ready JSON is unchanged. For the #66 error-code/receipt compatibility and rollback, see the [migration documentation](../database/migrations.md#66-release-compatibility-notes). Verification: `curl http://localhost:5020/` returns the running banner; `http://localhost:5020/swagger` (Development) is visible.

Frontend, choose one: `cd frontend && npm run dev` (8090, proxying `/api/*` to 5020), or `npm run build` and copy `dist/*` into `backend/Admin.WebApi/wwwroot/` for integrated mode.

## Step 4: seed script (fixture data)

```bash
cd <repository root>
USE_LOCAL_OSS=1 \
OSS_LOCAL_PATH=data/oss \
ConnectionStrings__AuditDb="$ConnectionStrings__AuditDb" \
dotnet run --project backend/Tools/LocalRegressionStubs -- seed
```

The seed writes three kinds of content (all deterministic; **running it twice yields the same result**):

| Category | Contents |
|------|------|
| Local object directory | 5 objects + 3 thumbnails each (`uploads/regression/...`, `mistakes/regression/...`) |
| `ruoyu_admin` | 1 completed audit run (`TriggerType=regression-seed`) + 4 pending (Status=0) records + 1 ignored (Status=2) record |
| Fixture JSON | `data/regression/fixtures.json` (the data source for stand-in serve mode) |

The old Status 0/2 in the seed stay historical; the current view has no select or delete operations. A complete v1 reference proof makes single/non-empty batch resolve return 409 `cleanup_not_authorized`; missing configuration or incomplete sources give 502 `references_unavailable`, with objects and audit records left as-is. status 3 is the new scan's read-only "no reference observed" observation, which cannot be ignored or turned into a deletion permission.

Refusal and failure behavior: refuses to run with a hint when `USE_LOCAL_OSS` is not `1`; refuses when the connection string is missing; fails with "start Admin.WebApi once first to create the tables" when the tables do not exist.

## Step 5: local stand-ins (serve mode)

```bash
dotnet run --project backend/Tools/LocalRegressionStubs -- serve
# optional: --mistake-outage (or MISTAKE_STUB_OUTAGE=1) simulates Mistake being unreachable, see below
```

A single process listens on :5005 / :5007 / :5009, implementing only the retained old HTTP client DTO queries; the actual audit worker/controller no longer calls these queries, always using the StorageReferences shared collector:

| Port | Endpoint | Consumer |
|------|------|--------|
| 5005 | `GET /api/uploads?page=&pageSize=` (`items[].imageEntries[].path` + `totalCount`) | `StudentHttpClient.GetAllUploadRecordsAsync` |
| 5007 | `GET /api/mistakes?page=&size=` (`items[].sourceRegions[].sourceImagePath` + `pageMeta.totalCount`) | `MistakeHttpClient.GetMistakeItemListAsync` |
| 5009 | `GET /api/admin/storage/image-references?page=&size=` (`success` envelope + `data.paths[]` + `data.hasMore`) | `HomeworkReferenceClient.GetAllImagePathsAsync` |

The stand-ins do not simulate downstream authentication, rate limiting, or error shapes. Their responses' binding to the ServiceClients DTOs is locked in CI by `backend/Tests/Services/LocalRegressionStubsContractTests.cs`. Verify:

```bash
curl -s 'http://localhost:5005/api/uploads?page=1&pageSize=100'   # items[].imageEntries[].path
curl -s 'http://localhost:5007/api/mistakes?page=1&size=100'      # items[].sourceRegions[].sourceImagePath
curl -s 'http://localhost:5009/api/admin/storage/image-references?page=1&size=200'
```

## Step 6: current audit regression boundary

The old stand-ins do not implement StorageReferences v1. With only the old Student/Mistake/Homework URLs configured, a trigger can be accepted but the Run fails as Status2 with zero S3 listing; resolve gives 502. You cannot restore "success" through an empty-set fallback or by reusing the old offset queries. Real report regression follows [StorageAudit](../modules/OssAudit/StorageAudit.md) to configure the three production providers with normal TLS, explicitly enabling the dedicated service trust; a successful Run saves all metadata while every resolve is still 409.

The worker no longer runs the startup cleanup of old Homework review images or non-audit bucket records; objects matching the old path shapes and the original audit history are also kept. The daily `OssAudit:ScheduledHour/Minute` still triggers the same report scan; any source failure aborts before listing; a successful scan does not re-insert an existing ObjectPath.

## FAQ

The current Mistake outbound session regression does not use the old anonymous stand-ins above to prove authorization. `MistakeService:UseSessionToken` defaults to false; true requires an explicit root origin (path `/`), http and https accepted equally (structural rules unchanged, localhost forbidden). Run `dotnet test backend/Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~MistakeSession"` to verify all 14 methods and the real Program handshake, ticket rotation/revocation after caching, concurrency, unknown results, cancellation, and zero later Student/S3 writes; the fake Authority and TCP receivers prove only the automated boundary. The final version must still use the official SignaCore, real current Mistake/Student, and actual S3 image retrieval in an owned isolated environment. The strict target reviewer/SourceIntake/managed immutable/pins protections must not be disabled for regression; a real target 401/403 is a safe 502 for Admin, failures/cancellations are not retried automatically, and an unknown write result must not be claimed uncommitted. For configuration and rollback see [Deployment](./Deployment.md#mistake-outbound-session-ikjnxa).

- **`InvalidOperationException` thrown at startup**: `IdentityService:AppId/AppSecret` not injected (step 2).
- **Startup throws `InvalidOperationException: IdentityService:Authority` (or `AdminOidc:RedirectUri`)**: the Authority/Issuer/Redirect used the `localhost` host name — `IsSafeUri`'s structural rule rejects that host name in every environment; use `127.0.0.1` instead. The latter exception is also thrown when `AdminOidc__RedirectUri` / `AdminOidc__PostLogoutRedirectUri` were never exported (both keys are required, see step 2).
- **Authentication validation failure mentioning RequireHttpsMetadata**: the Authority/Issuer used `http://` without setting `IdentityService:RequireHttpsMetadata=false`.
- **Login succeeds but APIs give 403 / no admin permissions**: the test user is not in the `AdminPortal:AdminUserIds` allowlist.
- **Seed fails with "tables do not exist"**: finish step 3 first so the shared startup gate completes the baseline migration.
- **Audit runs always `Status=2`**: StorageReferences not configured/enabled, or any of the three normal-TLS production providers failed contract, identity, paging, or connection; the old stand-ins do not satisfy this contract.


## Optional OIDC basic regression (#38)

The #24 stub/mock recipe above describes the old backend password/JWT and can be paired with a matching old SPA regression; it is not the joint acceptance of the current hosted login. The current SPA must follow [LocalSetup.md](./LocalSetup.md#hosted-login-development-configuration) to configure the required authentication parameters, using the official Provider with the real Identity/Teacher/Assistant downstream. Vite's callback and post-logout must use the browser origin `http://127.0.0.1:8090`; only direct integration with the backend uses 5020 — neither registers localhost. A production HTTPS proxy must keep the BFF seeing HTTPS (there is currently no forwarded-header trust), use an HTTPS upstream with a normal CA, and verify both CSRF entries; the OSS public proxy keeps the original signed bucket/key. For the full configuration and the query-logging prohibition, see [Deployment.md](./Deployment.md#signacore-hosted-login-the-only-mode). Secrets still go only through environment variables or user-secrets.

The automated handshake of the real Program is provided in isolation by `OidcTestAuthority` with Discovery/JWKS/token, a test RSA, and a fictitious canary — run `dotnet test backend/Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~Oidc"` (Docker required). It covers strict signatures and callback refusals, cancellation, single-use/concurrency, Cookie references/in-memory tokens, expiry/restart, anonymous SPA/health, and that no new Cookie admits the Admin API when the default switches are false; it does not imply the production ADMIN Code registration and downstream audience (Ruoyu.Study IKJ8MO) are ready.

The Session API suite uses valid required authentication configuration with a fake OIDC: the real Program covers the administrator session/native image, 401/403, rejection of any Authorization, missing server token/strict deadlines, GET csrf's Cookie/header/body binding, all write methods (including DELETE), concurrency and cancellation, and the retired 410 password. The five legacy keys' false/malformed inputs verify startup refusal, and absent/canonical true only validates migration input. The dedicated command is `dotnet test backend/Ruoyu.Admin.sln --configuration Release --filter "FullyQualifiedName~SessionApi_"`, using an isolated database and mock OSS, deleting no production objects. Audit resolve keeps the three-provider collector with full 409/unreachable 502 and zero Delete.

The current version runs only the server-session hosted login. You must configure `IdentityService:Authority/AppId/AppSecret`, `AdminOidc:RedirectUri/PostLogoutRedirectUri`, the current administrator allowlist and the ADMIN application audience, and complete real downstream trust plus exact Code/Logout registration. The five legacy keys `AdminOidc:Enabled/UseSessionForAdminApi/UseSessionForLogout/UseSessionForIdentityProxy/UseSessionForPortalProxies` no longer select the run mode: remove them; only omission or the canonical lowercase `true` passes migration validation, and an explicit `false`, empty, or malformed value is rejected at startup in every environment. The password entry point is permanently `410 legacy_login_disabled`; API/images/the three proxies/association queries always use the same administrator session and CSRF boundary; any inbound Authorization gets a fixed 401; there is no capability switch and no browser-JWT fallback mode.

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
