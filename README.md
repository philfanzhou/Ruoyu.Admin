# Ruoyu.Admin

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)

Ruoyu.Admin is the administration console for the Ruoyu.Study English-learning platform. It combines a .NET BFF API with a Vue 3 / Element Plus single-page app, shipped as one container.

It owns almost no business data. It authenticates administrators, aggregates and proxies the platform's downstream services, and owns exactly one domain of its own: **Storage Audit** — collecting complete immutable reference snapshots and reporting objects not observed in those captures. StorageReferences v1 never authorizes deletion.

> **Not a standalone product.** Ruoyu.Admin is an administration layer. It requires a running Identity provider and the Ruoyu.Study Student / Mistake / Homework / Teacher Portal / Assistant Portal services to do anything useful. See [Dependencies](#dependencies).

## Capabilities

- **Student management** — list, create, update and delete student records; configure which subjects each student has opened.
- **Account linking** — bind and unbind Identity accounts to student records, with batch account lookup.
- **Upload record review** — browse all learning-image upload records, rotate or remove images, reset status, delete after review.
- **Mistake management** — query, edit and submit mistake items; manually trigger visual-language analysis.
- **Storage Audit** — scheduled and on-demand runs that validate all three complete Student / Mistake / Homework reference snapshots before enumerating the object store and persist read-only unreferenced observations. Every resolve path refuses cleanup.
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
             ├── StorageReferenceCollector ► Student / Mistake / Homework (HTTPS + dedicated RS256)
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
| `backend/Ruoyu.Admin.Common` | `IOssService` (S3 + local-file), thumbnails, shared JWT utilities (not registered by this host), database initializer, shared constants |
| `backend/Ruoyu.Admin.Consul` | Consul KV configuration source with local cache fallback, PostgreSQL connection-string factory |
| `backend/Ruoyu.Admin.ServiceClients` | Hand-written HTTP clients and mirror DTOs for the Student and Mistake services |
| `backend/Tests` | xUnit + Moq + FluentAssertions unit tests |

`Ruoyu.Admin.ServiceClients` holds **copies** of the downstream DTO shapes, not shared definitions. The Student and Mistake HTTP contracts are owned by Ruoyu.Study; an upstream change surfaces here as a deserialization mismatch, not a compile error.

## Dependencies

| Dependency | Why |
|------------|-----|
| [SignaCore](https://github.com/philfanzhou/SignaCore) (or any compatible Identity service) | Hosted Code + PKCE identity provider and server-side gateway/account lookup |
| Ruoyu.Study Student service | Student records, upload records, presigned image URLs |
| Ruoyu.Study Mistake service | Mistake items, review submission, analysis triggers |
| Ruoyu.Study Homework service | Image-reference aggregation for Storage Audit |
| Ruoyu.Study Teacher Portal / Assistant Portal | Teacher and assistant administration, reached through the proxies |
| PostgreSQL | `ruoyu_admin` database — `OssAuditRuns`, `OssAuditRecords` |
| S3-compatible object store (SeaweedFS in the reference deployment) | Storage Audit read-only object browsing |
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
./start.sh --image ruoyu.admin:<version> --container ruoyu-admin --env-file /private/admin.env \
  --authority https://identity.example.com \
  --redirect-uri https://admin.example.com/api/auth/oidc/callback \
  --post-logout-redirect-uri https://admin.example.com/api/auth/oidc/logout-callback
```

`start.sh` first runs the selected image with `--validate-auth-config` and the same effective configuration; failure preserves the existing container. Credentials come from the deployment environment or a private env file, never output. `--config-file` mounts an explicit appsettings file; `--cache-dir` mounts an existing cache read-only during preflight and writable during normal startup. Port **5020** remains the container API port. See [Deployment](docs/development/Deployment.md#认证只读预检与升级) for target validation and the upgrade checklist.

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
| `AdminOidc:RedirectUri`, `AdminOidc:PostLogoutRedirectUri` | Required exact registered login and same-origin logout callbacks |
| Legacy five `AdminOidc` switches | Remove them; absent or canonical lowercase `true` is accepted only for migration validation, false/malformed values reject startup in every environment |
| Session token expiry | Valid expired access tokens require explicit reauthentication; no refresh or write replay |
| `AdminWeb:AllowedOrigins` | CORS origins; empty means allow any |
| `Oss:*` | `InternalEndpoint` / `InternalSecure` for direct S3 access, `PublicBaseUrl` for presigned URLs |
| `StorageReferences:*` | Default-off dedicated RS256 signing key and three mandatory HTTPS provider roots; see [StorageAudit](docs/modules/OssAudit/StorageAudit.md) |
| `OssAudit:ScheduledHour`, `OssAudit:ScheduledMinute` | Daily audit schedule (UTC) |
| `ConnectionStrings:AuditDb`, `Database:Name`, `PostgreSql:*` | Audit database |
| `Database:AllowCreate` | Allow startup to create a verifiably missing `ruoyu_admin` database (default `false` = refuse) |
| `Consul:*` | KV address, prefix, cache directory |

Full details: [docs/development/Deployment.md](docs/development/Deployment.md).

The SPA uses the server-session Code + PKCE flow: credentials stay on the hosted SignaCore page, tokens stay in a single-process ticket, and the browser carries an opaque HttpOnly cookie. Configure the required authority/client credentials, exact login/logout callbacks, current administrator allowlist and downstream ADMIN audience. Server sessions are the only inbound mode: no Bearer/JWT-cookie scheme is registered, and any Authorization header rejects protected business paths. Missing or invalid authentication configuration refuses startup instead of starting a disabled instance. Preflight uses the same appsettings, Consul snapshot/cache, environment and command-line precedence as normal startup, emits only fixed safe codes and exits before logging, host/database/worker/listener startup; it never refreshes or rewrites cache. Rollback requires a previously working complete integrated image and matching configuration; it cannot resurrect revoked tickets or obsolete browser tokens. For production session mode, HTTPS must reach the BFF as well as the browser. The host does not trust arbitrary forwarded scheme headers: an outer TLS proxy forwarding plain HTTP to 5020 cannot satisfy secure antiforgery cookies. Keep port 5020 and add a Kestrel HTTPS endpoint if needed; verify its certificate with the normal CA at the proxy. See [Deployment](docs/development/Deployment.md#signacore-托管登录唯一模式) for configuration and both CSRF probes. Tickets expire absolutely after eight hours, access-token expiry requires explicit reauthentication, and restarts lose sessions. There is no database migration, refresh, or automatic write replay.

## Tests

```bash
cd backend
dotnet test Ruoyu.Admin.sln --configuration Release
```

```bash
cd frontend
npm ci
npm test
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

StorageReferences v1 requires complete immutable snapshots from Student, Mistake and Homework. Any failed or incomplete source fails the run before S3 listing. Successful runs record three snapshot proofs and read-only `UnreferencedObservation` records. All single and nonempty batch resolve requests use the same collector and return 409 when complete or 502 when unavailable; neither deletes objects or records. Startup preserves legacy review images and historical `questions` / `documents` records. Questions and Documents remain outside the audited bucket set because they lack complete reference providers.

The original parent work still owns physical garbage collection and any future writer/collector deletion fence. Snapshots describe capture-time facts and cannot prove that a later writer has not created a reference. Deployment, migration and rollback boundaries: [StorageAudit](docs/modules/OssAudit/StorageAudit.md).

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

Password login (`POST /api/auth/login`) is permanently retired with `410 legacy_login_disabled`, before authentication or body binding in every configuration. Use SignaCore hosted login through `/api/auth/oidc/start`; invalid OIDC configuration refuses startup. The role callback is retained. Deploy the final combined version with the hosted-login SPA (#41); intermediate images containing the old password form are not release candidates.

Teacher/Assistant proxies now use only trusted server-session tokens. Both portals share path/body preservation, credential and upstream-cookie isolation, and request cancellation. Linked-account aggregation shares the same session boundary, uses server tokens and preserves partial-result behavior.

The Identity proxy accepts only a trusted server-session access token. It always strips browser CSRF/Host and forged gateway headers, injects owned AppId/AppSecret, suppresses upstream Set-Cookie, and propagates request cancellation without application retries.

Management APIs (including native images), ordinary CSRF, all three proxies and association queries always use the same administrator server-session boundary. Prepared logout always owns logout routes; non-POST logout returns 405. Anonymous SPA/static/health/claims callback remain reachable; API FallbackPolicy still requires authentication. Deploy only a complete API+SPA image, after the selected image passes read-only authentication preflight and the controlled end-to-end matrix.
