# Ruoyu.Admin

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)

Ruoyu.Admin is the administration console for the Ruoyu.Study English-learning platform. It combines a .NET BFF API with a Vue 3 / Element Plus single-page app, shipped as one container.

It owns almost no business data. It authenticates administrators, aggregates and proxies the platform's downstream services, and owns exactly one domain of its own: **Storage Audit** — finding and disposing object-storage files that no business record references any more.

> **Not a standalone product.** Ruoyu.Admin is an administration layer. It requires a running Identity provider and the Ruoyu.Study Student / Mistake / Homework / Teacher Portal / Assistant Portal services to do anything useful. See [Dependencies](#dependencies).

## Capabilities

- **Student management** — list, create, update and delete student records; configure which subjects each student has opened.
- **Account linking** — bind and unbind Identity accounts to student records, with batch account lookup.
- **Upload record review** — browse all learning-image upload records, rotate or remove images, reset status, delete after review.
- **Mistake management** — query, edit and submit mistake items; manually trigger visual-language analysis.
- **Storage Audit** — scheduled and on-demand runs that enumerate the object store, aggregate every path referenced by Student, Mistake and Homework, and raise orphan objects as audit records for delete / ignore / keep resolution.
- **Console proxying** — reverse-proxies Identity, Teacher Portal and Assistant Portal APIs so the SPA has a single origin.
- **Teacher and assistant administration** — reached through the Teacher Portal and Assistant Portal proxies.

## Architecture

```
Browser ── Vue 3 SPA (Element Plus, served from wwwroot)
             │  same origin
             ▼
        Admin.WebApi  :5020
             │
             ├── /api/admin/*            own controllers
             ├── /api/identity/*    ──►  Identity            :5002  (proxy + X-Admin-AppId/AppSecret)
             ├── /api/teacher-portal/*►  Teacher Portal      :5004  (proxy, bearer passthrough)
             ├── /api/assistant-portal/*► Assistant Portal   :5021  (proxy, bearer passthrough)
             │
             ├── IStudentHttpClient ──►  Student             :5005
             ├── IMistakeHttpClient ──►  Mistake             :5007
             ├── HomeworkReferenceClient ► Homework           :5009
             ├── IOssService (S3)     ──►  SeaweedFS          :8333
             └── AuditDbContext       ──►  PostgreSQL ruoyu_admin
```

### Error responses

Unhandled exceptions on JSON API endpoints answer `application/problem+json` with exactly `type`, `title`, `status`, `correlationId` and `errorCode` — never an exception message, stack or internal detail. A downstream `HttpRequestException` maps to `502 downstream.unavailable`; anything else answers the fixed `500 http.internal_server_error`. Endpoint-returned business responses (validation 400/404/409 and fixed-text catches), auth 401/403, proxied responses, the SPA, image redirects and health endpoints keep their existing bodies. Caller cancellation propagates; a response that already started is left exactly as sent. The SPA normalizes both formats through a single helper (`extractApiErrorMessage` in `frontend/src/services/apiBase.ts`: business `message` first, then the problem `title` when the content type is `application/problem+json`). Full contract: [docs/development/ErrorHandling.md](docs/development/ErrorHandling.md).

### Security response headers

JSON API responses carry the ServiceMantle six-header baseline (`Cache-Control: no-store`, `Pragma: no-cache`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and a `default-src 'none'` CSP) on every routed response of a marked endpoint — success, business 4xx, 401/403 challenges, 5xx and middleware short-circuits alike, as long as headers have not started. Marked surface: the ten JSON controllers annotated with `[RequireSecurityResponseHeaders]` plus the middleware-owned auth routes (`/api/auth/oidc/callback`, `/api/auth/logout/csrf`, `/api/auth/oidc/logout-callback`) whose marker-only route endpoints exist to carry metadata. Deliberately unmarked: `ImageController` (browser-native image redirects), the three proxy middlewares' forwarded responses, the SPA / static files, and the health endpoints.

### Backend projects

| Project | Purpose |
|---------|---------|
| `backend/Admin.WebApi` | Host, controllers, proxy middleware, audit persistence, `OssAuditWorker` |
| `backend/Ruoyu.Admin.Common` | `IOssService` (S3 + local-file), thumbnails, JWT bearer auth, database initializer, shared constants |
| `backend/Ruoyu.Admin.Consul` | Consul KV configuration source with local cache fallback, PostgreSQL connection-string factory |
| `backend/Ruoyu.Admin.ServiceClients` | Hand-written HTTP clients and mirror DTOs for the Student and Mistake services |
| `backend/Tests` | xUnit + Moq + FluentAssertions unit tests |

`Ruoyu.Admin.ServiceClients` holds **copies** of the downstream DTO shapes, not shared definitions. The Student and Mistake HTTP contracts are owned by Ruoyu.Study; an upstream change surfaces here as a deserialization mismatch, not a compile error.

## Dependencies

| Dependency | Why |
|------------|-----|
| [SignaCore](https://github.com/philfanzhou/SignaCore) (or any compatible Identity service) | Issues the JWTs this console accepts, and serves account lookup |
| Ruoyu.Study Student service | Student records, upload records, presigned image URLs |
| Ruoyu.Study Mistake service | Mistake items, review submission, analysis triggers |
| Ruoyu.Study Homework service | Image-reference aggregation for Storage Audit |
| Ruoyu.Study Teacher Portal / Assistant Portal | Teacher and assistant administration, reached through the proxies |
| PostgreSQL | `ruoyu_admin` database — `OssAuditRuns`, `OssAuditRecords` |
| S3-compatible object store (SeaweedFS in the reference deployment) | Storage Audit browsing, orphan cleanup, migration assistance |
| Consul (optional) | KV-backed configuration with local cache fallback |
| Grafana Loki (optional) | Log aggregation |

## Run locally

Requirements: .NET SDK 10.0 and Node.js 20 or later.

```bash
cd backend
dotnet restore Ruoyu.Admin.sln
dotnet run --project Admin.WebApi/Admin.WebApi.csproj
```

The API listens on port **5020**, which is hardcoded. `IdentityService:AppId` and `IdentityService:AppSecret` are mandatory — the host refuses to start without them.

In another terminal:

```bash
cd frontend
npm ci
npm run dev
```

The dev server listens on port 8090 and proxies every `/api/*` request (including `/api/auth/*` for login and logout) to `http://localhost:5020`.

Set `USE_LOCAL_OSS=1` (and optionally `OSS_LOCAL_PATH`) to run Storage Audit against a local directory instead of S3.

## Run with Docker

The integrated image serves the API and the built SPA from one container:

```bash
./scripts/build.sh
IDENTITY_APP_ID=... IDENTITY_APP_SECRET=... ./start.sh
```

`start.sh` maps host port **5020** to container port 5020 (host port equals container port, matching the platform's other APIs), and expects `CONSUL_HTTP_ADDR`, `IDENTITY_APP_ID` and `IDENTITY_APP_SECRET` from the deployment environment.

### Database startup (migrations)

The `ruoyu_admin` schema is managed by an EF Core baseline migration executed by the ServiceMantle 0.3.0 startup database gate and shared orchestrator:

1. **Target preparation** — an existing database is used as-is; a verifiably missing database is created only when `Database:AllowCreate=true` (default `false` — otherwise startup refuses with the fixed code `database_target_preparation.creation_not_allowed` and writes nothing). Back up the database before upgrading.
2. **Advisory-lock orchestration** — startup acquires a PostgreSQL session advisory lock (30 s acquire budget) so that when multiple instances start concurrently exactly one executes the migration; the others re-inspect under the lock and skip.
3. **Takeover rules** — an empty database gets the baseline applied; a verified legacy database (created by the retired inline DDL, structure checked column by column) is taken over with its data preserved and only the migration history stamped; a history holding unknown migration ids or any unknown/conflicting structure fails startup with `migration.version_too_new` / `migration.inspection_failed` (executor code `RUOYU_ADMIN_DB_SCHEMA_INCOMPATIBLE`) — never auto-repaired. Every orchestration failure exits non-zero with a safe error code; logs never contain connection strings or credentials.

The shared lifecycle gate runs once before the web host or audit worker starts. Configuration is validated before database I/O; invalid creation switches or unparsable connections fail with `database_target_preparation.invalid_target`. The process-local receipt progresses from NotStarted to Running, then Succeeded or Failed; cancellation keeps Running. Readiness makes no database calls until Succeeded. The mapped-schema probe proves only readable mapped tables and columns, not constraints, indexes, or data correctness.

The startup error codes changed in #66: missing-target refusal now uses `database_target_preparation.creation_not_allowed` (previously `RUOYU_ADMIN_DB_CREATION_NOT_ALLOWED`), invalid `Database:AllowCreate` uses `database_target_preparation.invalid_target` (previously `RUOYU_ADMIN_DB_ALLOW_CREATE_INVALID`), and an unconnectable target after preparation uses `database_target_preparation.not_connectable_after_preparation`. Fresh receipts now report NotStarted; failed receipts report Failed / `ruoyu-admin.startup_failed`. Running, Succeeded, normal readiness JSON and migration/schema refusal codes stay unchanged. Rolling back restores the former codes without a schema rollback and does not undo created databases or committed migrations. See [migration compatibility notes](docs/database/migrations.md#66-发布兼容说明).

### Health probes

Three anonymous JSON endpoints are served alongside the SPA (never rewritten by the SPA fallback):

- `GET /health/live` — always 200 while the process runs; use for liveness/restart probes.
- `GET /health/ready` (alias `GET /health`) — ready (200) only when this process finished its database initialization **and** a bounded (3 s) read-only probe of the `ruoyu_admin` tables succeeds; every failure answers 503 with a fixed safe `errorCode` (`ruoyu-admin.startup_incomplete`, `ruoyu-admin.startup_failed`, `ruoyu-admin.database_unreachable`, `ruoyu-admin.schema_unavailable`, `health.probe_timeout`, `health.probe_failed`) and never leaks connection strings or exception text. Each request re-samples, so a recovered database is ready again on the next probe. Downstream services are deliberately excluded from readiness to avoid cascading removal. Safe to wire into readiness gates and traffic gating.

## Configuration

Configuration is read from `appsettings.json`, then Consul KV under `config/ruoyu` (when reachable), then environment variables. Consul results are cached locally so the host still starts when Consul is down.

| Section | Purpose |
|---------|---------|
| `IdentityService:*` | Identity authority, `AppId` / `AppSecret` for gateway calls |
| `StudentService:Url`, `MistakeService:Url`, `HomeworkService:Url` | Downstream service addresses |
| `TeacherPortal:Url`, `AssistantPortal:Url` | Proxy targets |
| `AdminPortal:AdminUserIds` | Accounts granted the `admin` role via the Identity callback |
| `AdminOidc:Enabled`, `AdminOidc:RedirectUri` | Optional hosted-login handshake (disabled by default), exact registered callback |
| `AdminOidc:UseSessionForLogout`, `AdminOidc:PostLogoutRedirectUri` | Optional prepared logout (default false, requires OIDC and session API); exact registered same-origin `/api/auth/oidc/logout-callback` |
| Session token expiry | Session API returns `reauthentication_required` for a valid expired token; `/api/auth/session` exposes `requiresReauthentication`; sign-in is explicit with no refresh or write replay |
| `AdminOidc:UseSessionForPortalProxies` | Teacher/Assistant proxy server tokens (default false, requires OIDC and session API); association queries follow the API switch independently |
| `AdminOidc:UseSessionForIdentityProxy` | Identity proxy server-token authorization (default false, requires OIDC and session API); isolates browser cookies/CSRF and upstream Set-Cookie |
| `AdminOidc:UseSessionForAdminApi` | Optional session authorization and CSRF for `/api/admin/*` (default false, requires OIDC); retires password login when true |
| `AdminWeb:AllowedOrigins` | CORS origins; empty means allow any |
| `Oss:*` | `InternalEndpoint` / `InternalSecure` for direct S3 access, `PublicBaseUrl` for presigned URLs |
| `OssAudit:ScheduledHour`, `OssAudit:ScheduledMinute` | Daily audit schedule (UTC) |
| `ConnectionStrings:AuditDb`, `Database:Name`, `PostgreSql:*` | Audit database |
| `Database:AllowCreate` | Allow startup to create a verifiably missing `ruoyu_admin` database (default `false` = refuse) |
| `Consul:*` | KV address, prefix, cache directory |

Full details: [docs/development/Deployment.md](docs/development/Deployment.md).

The optional SignaCore Code + PKCE handshake stores tokens in a single-process server ticket, with an opaque HttpOnly cookie and an absolute eight-hour lifetime. This phase has not switched the SPA or API/proxy authorization: existing JWT login remains active, and the new cookie alone does not grant Admin API access. Production activation requires the exact HTTPS callback registration and downstream audience migration; restarts lose pending handshakes and sessions.

## Tests

```bash
cd backend
dotnet test Ruoyu.Admin.sln --configuration Release
```

```bash
cd frontend
npm run build
```

## Releases

Push a tag to publish container images to GHCR. Tags carry no `v` prefix and are
validated against `MAJOR.MINOR.PATCH(-rc.N)`; images are published only after
`Build & Test` passes on the tag.

| Tag | Published tags per image | Channel |
|-----|--------------------------|---------|
| `X.Y.Z-rc.N` | `X.Y.Z-rc.N` only | Test build. Immutable tag, never moves a production tag; GitHub Release marked pre-release |
| `X.Y.Z` | `X.Y.Z`, `X.Y`, `latest` | Stable release; GitHub Release marked latest |

Image: `ghcr.io/philfanzhou/ruoyu.admin` — the unified API + SPA single
container (`backend/Admin.WebApi/Dockerfile`, repository root as context),
the only shape any deployment has ever run, published with provenance and
SBOM attestations. The standalone frontend image inherited from the monorepo
was retired unused: no environment ever ran the split mode.

`main` is protected by a ruleset: changes land through pull requests that pass
the required checks (`Build & Test`, `Analyze (csharp)`,
`Analyze (javascript-typescript)`); direct pushes, force-pushes and branch
deletion are blocked.

## Documentation

| Entry point | Contents |
|-------------|----------|
| [docs/README.md](docs/README.md) | Documentation index |
| [docs/overview/](docs/overview/) | Service boundary, requirements, design, key flows |
| [docs/modules/](docs/modules/) | Capability documents |
| [docs/Integration/](docs/Integration/) | External system contracts |
| [docs/database/](docs/database/) | Audit schema and data ownership |
| [docs/development/](docs/development/) | Local setup, run and debug, deployment, verification |
| [docs/pending-decisions.md](docs/pending-decisions.md) | Open design decisions |
| [CONTEXT.md](CONTEXT.md) | Storage Audit ubiquitous language |

## Known issues

**Storage Audit used to misclassify every QuestionBank image as an orphan — fixed during the extraction.** `OssAuditWorker` scanned `uploads/`, `mistakes/` and `questions/` but only aggregated referenced paths from Student, Homework and Mistake. Nothing covered `questions/`, so every object there was recorded as unreferenced; the pre-delete revalidation queries the same sources and therefore could not object, and resolving those records called `IOssService.DeleteAsync` — permanently deleting question images along with their thumbnails.

The audit scope is now `OssAuditWorker.AuditedBuckets` (`Uploads`, `Mistakes`) under the invariant that a bucket may only be scanned when a reference source covers it, and stale `questions` / `documents` records are purged on startup. `Mistake` unavailability now aborts the run instead of degrading, matching Student and Homework.

`OssAuditController` also had **no test coverage at all** — the monorepo documentation listed twenty-two such tests as implemented, and they did not exist there either. `OssAuditControllerTests` now covers every refusal branch of the pre-delete revalidation (31 cases), including case-insensitive matching, the null `HomeworkReferenceClient` path, and whole-batch refusal when a reference source is unreachable. The two 502-guard cases were A/B verified against the guard removed. Writing them surfaced a second defect: `BatchResolve`'s empty-result early return omitted `totalRequested`, which `docs/api.md` and the frontend's `BatchResolveResponse` both declare as required; the two paths now agree.

> **Upgrade note:** the purge runs at application startup. If your database already holds records with `Bucket = 'questions'`, do not resolve or batch-resolve them on the old version — upgrade first and let the startup cleanup remove them.

**`Steeltoe.Discovery.Consul` 4.2.0 carries a known high-severity advisory** (GHSA-67c9-f6v2-qv86: malformed `secure` metadata aborts service instance lookup — a denial of service; patched in 4.3.0). Inherited from the monorepo; `dotnet restore` surfaces it as NU1903. The CI image scans run report-only (`exit-code: '0'`) until the bump lands: the advisory has a patched release, so `--ignore-unfixed` would not skip it and a blocking scan would fail every pull request today. The bump needs its own verification of Consul KV loading and service registration, and promoting the scans to blocking rides with that change.

**`AdminApi:Port` in `appsettings.json` is dead configuration**; nothing reads it. The listen port is the hardcoded `const int httpPort = 5020` in `Program.cs`.

**`Ruoyu.Admin.ServiceClients` carries unused downstream methods.** The library was shared with the Mistake service and several portals in the monorepo; this repository consumes 20 of Student's 25 methods and 7 of Mistake's 14. The remainder, including the mistake→student DTO block, is dead code here.

Everything except the Storage Audit fix is inherited from the monorepo rather than introduced by the extraction. Full list with fix options: [docs/README.md](docs/README.md).

## Project status

Extracted from the Ruoyu.Study monorepo in September 2026 with full subtree history preserved. It is in production use against the Ruoyu.Study platform, but it is **not** a general-purpose product: its downstream contracts are specific to that platform and are not versioned or published independently.

**Ruoyu.Study itself is not public.** It is therefore named in these docs but never linked, and the downstream HTTP contracts are described only as far as this repository consumes them. Configuration values that identify a specific deployment — Identity issuer and audience, the public object-storage base URL, Consul and service addresses — are placeholders in this repository and must be supplied by your own deployment configuration.

Known documentation debt carried over from the monorepo is listed in [docs/README.md](docs/README.md).

## License

MIT — see [LICENSE](./LICENSE).

Management APIs (including native images), ordinary CSRF and session status now require the enabled server-session capability: disabling it returns `503 session_api_disabled` before authentication. OIDC-disabled session status returns `503 oidc_disabled`. Logout is owned by prepared-logout middleware; disabling it returns `503 session_logout_disabled`, and enabled non-POST logout returns 405. Browser JWTs cannot restore these local capabilities. Deploy only the final combination with the hosted-login SPA (#41/#75).
