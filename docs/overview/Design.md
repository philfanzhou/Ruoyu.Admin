# Design document

> **Phase 4 planned changes (not yet implemented)**: this document has been updated to the Phase 4 target state. Main changes:
> - `StudentManagementGrpcServiceClient` is removed; all Student gRPC calls go through `StudentLearningGrpcServiceClient` uniformly
> - `IOssService` permissions are downgraded to read-only + limited write (zombie-cleanup Delete + migration-assist CopyObject)
> - Admin Portal implements the path aggregation logic itself (fetching data via generic gRPC pagination and aggregating)
> - The OSS audit uses direct `IOssService.ListObjectsAsync` + generic gRPC path aggregation

## Service-level architecture

### Layered architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Frontend (Vue 3)                        │
│                  Vue 3 + Element Plus + Vite                │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTP/JSON
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    ASP.NET Core 8.0                          │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                  Middleware layer                      │  │
│  │  ┌─────────────────┐  ┌────────────────────────────┐ │  │
│  │  │  CORS           │  │  IdentityProxyMiddleware    │ │  │
│  │  └─────────────────┘  └────────────────────────────┘ │  │
│  │  ┌─────────────────┐  ┌────────────────────────────┐ │  │
│  │  │  Swagger        │  │  TeacherPortalProxyMiddleware│ │  │
│  │  └─────────────────┘  └────────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                  Controller layer                      │  │
│  │  ┌──────────────────┐  ┌───────────────────────────┐ │  │
│  │  │ StudentsController│  │ MistakeController         │ │  │
│  │  └──────────────────┘  └───────────────────────────┘ │  │
│  │  ┌──────────────────┐  ┌───────────────────────────┐ │  │
│  │  │ OssAuditController│ │ OssUploadRecordController  │ │  │
│  │  └──────────────────┘  └───────────────────────────┘ │  │
│  │  ┌──────────────────┐  ┌───────────────────────────┐ │  │
│  │  │ ImageController   │  │ EnumOptionsController      │ │  │
│  │  └──────────────────┘  └───────────────────────────┘ │  │
│  │  ┌──────────────────┐                                 │  │
│  │  │IdentityAccounts  │                                 │  │
│  │  │  Controller      │                                 │  │
│  │  └──────────────────┘                                 │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                   Service layer                        │  │
│  │  ┌──────────────────┐  ┌───────────────────────────┐ │  │
│  │  │ OssAuditWorker   │  │ IOssService               │ │  │
│  │  │ (BackgroundService)│ │ (S3OssService /          │ │  │
│  │  │                  │  │  LocalFileOssService)     │ │  │
│  │  │ Path aggregation:│  │  [permissions downgraded: │ │  │
│  │  │  Student generic │  │   read-only +             │ │  │
│  │  │  gRPC            │  │   limited write           │ │  │
│  │  │  Mistake generic │  │   (Delete/Copy)]          │ │  │
│  │  │  gRPC            │  │  ListObjects (audit       │ │  │
│  │  │                  │  │    browsing)              │ │  │
│  │  │                  │  │  Delete (zombie cleanup)  │ │  │
│  │  │                  │  │  CopyObject (migration    │ │  │
│  │  │                  │  │    assistance)            │ │  │
│  │  └──────────────────┘  └───────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                    Data layer                          │  │
│  │  ┌──────────────────┐  ┌───────────────────────────┐ │  │
│  │  │ AuditDbContext   │  │ DatabaseInitializer       │ │  │
│  │  │ (EF Core)        │  │ (manual DDL)              │ │  │
│  │  └──────────────────┘  └───────────────────────────┘ │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

## Technology stack

| Layer | Technology | Version | Notes |
|------|------|------|------|
| Runtime | .NET | 8.0 | ASP.NET Core Web API |
| ORM | Entity Framework Core | 8.0 | Npgsql (PostgreSQL) provider |
| gRPC client | Grpc.Net.ClientFactory | 2.62.0 | Connects to the Student/Mistake services |
| Database | PostgreSQL | 12+ | Via Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11 |
| API docs | Swashbuckle.AspNetCore | 6.5.0 | Swagger UI (Development environment only) |
| Object storage | S3-compatible API / local files | - | Via the IOssService abstraction (permissions downgraded: read-only + limited write, for audit browsing ListObjects, zombie cleanup Delete, migration assistance CopyObject) |
| Frontend | Vue 3 + Element Plus | - | SPA, deployable into wwwroot |
| Build tooling | Vite | - | Frontend build |
| Testing | xUnit + Moq + FluentAssertions | - | Unit tests + coverlet coverage |

## Dependency diagram

```
                     Admin.WebApi (ASP.NET Core 8.0)
                                │
     ┌──────────────┬───────────┼──────────────┬─────────────────┐
     │              │           │              │                 │
     ▼              ▼           ▼              ▼                 ▼
Ruoyu.Admin.   Ruoyu.Admin.  Ruoyu.Admin.  IdentityProxy    TeacherPortalProxy
ServiceClients   Common        Consul      Middleware       AssistantPortalProxy
     │              │           │                          Middleware
     │              │           ├─ AddRuoyuConsulConfiguration
     │              │           ├─ UseRuoyuSerilog (Console + Loki)
     │              │           ├─ SharedPostgreSqlConnectionStringFactory
     │              │           └─ StartupDiagnosticsFormatter
     │              │
     │              ├─ IOssService
     │              │  ├─ S3OssService
     │              │  └─ LocalFileOssService
     │              │  [permissions downgraded: read-only + limited write]
     │              │  ListObjects / Delete / CopyObject
     │              │
     │              ├─ DatabaseInitializer
     │              │
     │              └─ Constants
     │                 ├─ GradeConstants
     │                 ├─ SubjectConstants
     │                 ├─ UploadStatusConstants
     │                 ├─ ClassificationConstants
     │                 └─ ReviewStatusConstants
     │
     ├─ IStudentHttpClient ──► Student HTTP  :5005
     ├─ IMistakeHttpClient ──► Mistake HTTP  :5007
     └─ HomeworkReferenceClient (Admin.WebApi's own) ──► Homework HTTP :5009
```

Downstream services forwarded by the proxy middleware:

| Middleware | Route prefix | Downstream | Authentication |
|--------|----------|------|------|
| `IdentityProxyMiddleware` | `/api/identity/*` | Identity :5002 | injects `X-Admin-AppId` / `X-Admin-AppSecret` |
| `TeacherPortalProxyMiddleware` | `/api/teacher-portal/*` | Teacher Portal :5004 | sends the in-ticket server access token, rejects inbound Authorization; downstream checks `role=admin` |
| `AssistantPortalProxyMiddleware` | `/api/assistant-portal/*` | Assistant Portal :5021 | same as above |

> **Protocol status**: all downstream calls are HTTP/JSON; gRPC has been removed entirely. The DTOs in `Ruoyu.Admin.ServiceClients` are mirror copies of the Student and Mistake services' external HTTP contracts; both are maintained by the Ruoyu.Study repository, which owns their definitions.

## Key design decisions

### 1. Database initialization: DatabaseInitializer instead of EF Migrations

**Decision**: use `DatabaseInitializer` + `EnsureCreated` instead of EF Core Migrations.

**Reasons**:
- The `OssAuditRuns` table needs `GENERATED ALWAYS AS IDENTITY`, which EF Core's `EnsureCreated` does not support
- The project's table structure is simple (only 2 tables) and needs no complex migration management
- `CREATE TABLE IF NOT EXISTS` semantics guarantee idempotent startup

### 2. Audit concurrency control: mutual exclusion via database records

**Decision**: mutual exclusion through the `Status=0` record in the `OssAuditRuns` table.

**Reasons**:
- No distributed lock needed
- Database-level atomicity guarantees
- Double checking (SaveChanges + query) for reliability

### 3. Proxy middleware instead of a BFF framework

**Decision**: implement the HTTP reverse proxy with custom middleware (`IdentityProxyMiddleware`, `TeacherPortalProxyMiddleware`).

**Reasons**:
- The proxy logic is simple (path mapping + authentication header injection)
- No heavyweight framework like YARP needed
- Precise control over request/response handling

### 4. IOssService abstraction (permission downgrade)

**Decision**: Admin Portal keeps `IOssService`, with permissions downgraded to read-only + limited write.

**Reasons**:
- Image downloads switched to presigned URLs from Student/Mistake gRPC `GetPresignedUrl` + 302 redirects
- Image migration switched to proxying through Student gRPC `MigrateImagesToMistake`
- `IOssService` is used for audit-scenario `ListObjectsAsync`, cleanup-scenario `DeleteAsync`, and migration-assist `CopyObjectAsync`
- Credential permissions downgraded to read-only + limited write (List + Delete + CopyObject allowed)

**Change impact**:
- `ImageController`: no longer uses `IOssService`; uses gRPC `GetPresignedUrl` instead
- `MistakeController.MigrateImages`: no longer uses `IOssService`; uses Student gRPC `MigrateImagesToMistake` instead
- `OssAuditWorker`: `ListOssObjectsPaged` now calls `IOssService.ListObjectsAsync` directly; path aggregation now fetches via generic gRPC pagination and aggregates locally
- `OssAuditController.ResolveRecord`: keeps `IOssService` for DeleteObject

**oss-path-autonomy changes**:
- `IOssService.DeleteAsync` automatically cleans associated thumbnails: `S3OssService.DeleteAsync` internally calls `ThumbnailHelper.IsThumbnailPath` to decide whether the path is a thumbnail; for an original image it calls `ThumbnailHelper.GetAllThumbnailPaths` to get all thumbnail paths and deletes each before deleting the original; for a thumbnail it deletes only that file itself, with no recursive cleanup
- Admin Portal's `S3OssService` configures no path-prefix validation (`allowedPrefixes` is null), because audit browsing and operations need access to arbitrary path prefixes, including questions/ and documents/, which are currently outside the audit scope
- Thumbnail path rule change: from `uploads/thumbnails/` to the same directory as the original image, with the path format `{dirname}/{stem}_{size}.jpg`
- `OssUploadRecordController`: switched to generic gRPC + direct OSS operations

### 4a. Admin Portal implements path aggregation itself (added in Phase 4)

**Decision**: Admin Portal implements the path aggregation logic of the OSS audit itself and no longer depends on dedicated downstream interfaces.

**Reasons**:
- `StudentManagementGrpcService.GetRegisteredOssPaths` is removed; Admin Portal fetches upload records paginated through Student generic gRPC (e.g. `GetAllUploadRecords`) and extracts and aggregates image paths itself
- `MistakeGrpcService.GetAllReferencedImagePaths` is removed; Admin Portal iterates mistake items paginated through Mistake generic gRPC (e.g. `GetMistakeItemList`) and extracts and aggregates image paths itself
- Path aggregation is an Admin-audit-specific need and should not require other services to expose dedicated interfaces

**Implementation**:
- Student path aggregation: call `StudentLearningGrpcService.GetAllUploadRecords` paginated to fetch all upload records, extract the `ImagePaths` field, and aggregate with deduplication
- Mistake path aggregation: call `MistakeGrpcService.GetMistakeItemList` paginated to iterate all mistake items, extract the `ImagePath` field, and aggregate with deduplication

### 5. Integrated frontend/backend deployment

**Decision**: support deploying the frontend build output into the backend's `wwwroot` directory, with ASP.NET Core serving the static files.

**Reasons**:
- Simpler deployment (a single service)
- SPA route fallback handling
- Support for runtime `__APP_TITLE__` replacement

### 5a. Public OSS routing

Admin Portal maintains no `/oss/` Nginx location. The Admin API uses `Oss:PublicBaseUrl` to generate download addresses signed with the public domain, and the browser follows the 302 to the platform's public OSS entry; in this repository only the User Web Nginx proxies that path.

Actual S3 operations use `Oss:InternalEndpoint`, so the Admin backend can connect directly to SeaweedFS on a separate server without depending on the Portal Nginx or Docker DNS. Full decision in Ruoyu.Study's ADR-0003 (separate-internal-oss-access-from-public-presigned-routing; that repository is not public) (a cross-repository decision owned by Ruoyu.Study).

### 6. How downstream HTTP contracts are referenced

**Decision**: no contract generation; instead, hand-written `IStudentHttpClient` / `IMistakeHttpClient` plus mirror DTOs inside `Ruoyu.Admin.ServiceClients`.

**History**: the early implementation referenced the Student / Mistake proto files in the monorepo directly via `<Protobuf>` to generate gRPC clients. gRPC has been fully retired in favor of HTTP/JSON, and the proto references were removed with it.

**Current approach**:
- `Ruoyu.Admin.ServiceClients` is a standalone class library depending only on `Microsoft.Extensions.Http` and the ASP.NET Core shared framework.
- One interface + one implementation + one set of DTOs per downstream service, with DTO fields mirroring the downstream HTTP responses one by one.
- Registered in `Program.cs` via `AddStudentHttpClient(url)` / `AddMistakeHttpClient(url)`.
- `HomeworkReferenceClient` is Admin's one-off aggregation client with a single method; it stays in `Admin.WebApi/Services/` and does not go into the shared library.

**Notes**:
- The DTOs are **copies**, not shared definitions. The Student / Mistake HTTP contracts are maintained by the Ruoyu.Study repository; their field changes produce no compile error in this repository — only runtime deserialization drift.
- Therefore downstream contract changes must be synchronized into `Ruoyu.Admin.ServiceClients`, with the corresponding controller unit tests added.
- The future direction is for downstream to publish OpenAPI specs and this repository to generate clients, eliminating the hand-written mirrors at the root.

## Configuration structure

All configuration is managed via `appsettings.json` + environment variables; see [Deployment.md](../development/Deployment.md) for details.

| Configuration section | Description |
|--------|------|
| `AdminApi:Port` | Service listen port |
| `ConnectionStrings:AuditDb` | Audit database connection string (local development default; production is overridden by Consul) |
| `Database:Name` | Database name (synthesized with Consul `PostgreSql:*` into the connection string, `ruoyu_admin`) |
| `StudentService:Url` | Student HTTP service address |
| `MistakeService:Url` | Mistake HTTP service address |
| `IdentityService:*` | Identity service address and credentials |
| `TeacherPortal:*` | Teacher Portal address and API key |
| `OssAudit:*` | Audit scheduled job configuration |
| `Oss:*` | OSS storage configuration (internal connection uses `InternalEndpoint/InternalSecure`, presigning uses `PublicBaseUrl`) |
| `AdminWeb:AllowedOrigins` | CORS-allowed origins |
