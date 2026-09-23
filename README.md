# Ruoyu.Admin

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](./LICENSE)

Ruoyu.Admin is the administration console for the [Ruoyu.Study](https://github.com/philfanzhou/Ruoyu.Study) English-learning platform. It combines a .NET 8 BFF API with a Vue 3 / Element Plus single-page app, shipped as one container.

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

### Backend projects

| Project | Purpose |
|---------|---------|
| `backend/Admin.WebApi` | Host, controllers, proxy middleware, audit persistence, `OssAuditWorker` |
| `backend/Ruoyu.Admin.Common` | `IOssService` (S3 + local-file), thumbnails, JWT bearer auth, database initializer, shared constants |
| `backend/Ruoyu.Admin.Consul` | Consul KV configuration source with local cache fallback, Serilog/Loki bootstrap, PostgreSQL connection-string factory |
| `backend/Ruoyu.Admin.ServiceClients` | Hand-written HTTP clients and mirror DTOs for the Student and Mistake services |
| `backend/Tests` | xUnit + Moq + FluentAssertions unit tests |

`Ruoyu.Admin.ServiceClients` holds **copies** of the downstream DTO shapes, not shared definitions. The Student and Mistake HTTP contracts are owned by [Ruoyu.Study](https://github.com/philfanzhou/Ruoyu.Study); an upstream change surfaces here as a deserialization mismatch, not a compile error.

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

Requirements: .NET SDK 8.0 and Node.js 20 or later.

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

The dev server listens on port 8090 and proxies `/api/identity`, `/api/admin`, `/api/teacher-portal` and `/api/assistant-portal` to `http://localhost:5020`.

Set `USE_LOCAL_OSS=1` (and optionally `OSS_LOCAL_PATH`) to run Storage Audit against a local directory instead of S3.

## Run with Docker

The integrated image serves the API and the built SPA from one container:

```bash
./scripts/build.sh
IDENTITY_APP_ID=... IDENTITY_APP_SECRET=... ./start.sh
```

`start.sh` maps host port **10901** to container port 5020, and expects `CONSUL_HTTP_ADDR`, `IDENTITY_APP_ID` and `IDENTITY_APP_SECRET` from the deployment environment.

To deploy the frontend separately behind Nginx:

```bash
./scripts/build-web.sh
```

## Configuration

Configuration is read from `appsettings.json`, then Consul KV under `config/ruoyu` (when reachable), then environment variables. Consul results are cached locally so the host still starts when Consul is down.

| Section | Purpose |
|---------|---------|
| `IdentityService:*` | Identity authority, `AppId` / `AppSecret` for gateway calls |
| `StudentService:Url`, `MistakeService:Url`, `HomeworkService:Url` | Downstream service addresses |
| `TeacherPortal:Url`, `AssistantPortal:Url` | Proxy targets |
| `AdminPortal:AdminUserIds` | Accounts granted the `admin` role via the Identity callback |
| `AdminWeb:AllowedOrigins` | CORS origins; empty means allow any |
| `Oss:*` | `InternalEndpoint` / `InternalSecure` for direct S3 access, `PublicBaseUrl` for presigned URLs |
| `OssAudit:ScheduledHour`, `OssAudit:ScheduledMinute` | Daily audit schedule (UTC) |
| `ConnectionStrings:AuditDb`, `Database:Name`, `PostgreSql:*` | Audit database |
| `Consul:*` | KV address, prefix, cache directory |

Full details: [docs/development/Deployment.md](docs/development/Deployment.md).

## Tests

```bash
cd backend
dotnet test Ruoyu.Admin.sln --configuration Release
```

```bash
cd frontend
npm run build
```

## Documentation

| Entry point | Contents |
|-------------|----------|
| [docs/README.md](docs/README.md) | Documentation index |
| [docs/overview/](docs/overview/) | Service boundary, requirements, design, key flows |
| [docs/modules/](docs/modules/) | Capability documents |
| [docs/Integration/](docs/Integration/) | External system contracts |
| [docs/database/](docs/database/) | Audit schema and data ownership |
| [docs/development/](docs/development/) | Local setup, run and debug, deployment, verification |
| [CONTEXT.md](CONTEXT.md) | Storage Audit ubiquitous language |

## Known issues

**Storage Audit misclassifies every QuestionBank image as an orphan.** `OssAuditWorker` scans the `uploads/`, `mistakes/` and `questions/` prefixes but only aggregates referenced paths from Student, Homework and Mistake. Nothing aggregates QuestionBank's question images, so every object under `questions/` is written to `OssAuditRecords` as unreferenced. Resolving those records calls `IOssService.DeleteAsync`, which **permanently deletes the files** along with their thumbnails.

Until this is fixed, do not resolve or batch-resolve audit records whose `Bucket` is `questions`.

`AdminApi:Port` in `appsettings.json` is dead configuration; nothing reads it. The listen port is the hardcoded `const int httpPort = 5020` in `Program.cs`.

Both issues are inherited from the monorepo, not introduced by the extraction. Details and fix options: [docs/README.md](docs/README.md).

## Project status

Extracted from the [Ruoyu.Study](https://github.com/philfanzhou/Ruoyu.Study) monorepo in September 2026 with full subtree history preserved. It is in production use against the Ruoyu.Study platform, but it is **not** a general-purpose product: its downstream contracts are specific to that platform and are not versioned or published independently.

Known documentation debt carried over from the monorepo is listed in [docs/README.md](docs/README.md).

## License

MIT — see [LICENSE](./LICENSE).
