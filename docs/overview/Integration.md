# Integration documentation

> **⚠️ This document is outdated as a whole and has not been re-verified.**
>
> The integration matrix below is still described in terms of **gRPC**, carrying the historical "Phase 4 planned changes (not yet implemented)" annotation. The actual implementation has fully moved to HTTP/JSON and gRPC has been removed entirely; the matrix is also **missing the Homework (:5009)** leg — `HomeworkReferenceClient` aggregates image references for Storage Audit and is one of the audit's required dependencies.
>
> The current implementation facts are in the code: `backend/Admin.WebApi/Program.cs` (downstream registration), `backend/Admin.WebApi/Controllers/` (external endpoints), the three `*ProxyMiddleware.cs` (proxy forwarding), `backend/Ruoyu.Admin.ServiceClients/` (Student / Mistake contract mirrors), and `backend/Admin.WebApi/Services/HomeworkReferenceClient.cs`.
>
> The Storage Audit rows in the "failure semantics summary" below have been corrected per the current code; the other rows may still be outdated. Re-verifying the whole matrix is a separate to-do.

## Observability foundation (ServiceMantle)

> This section is current implementation fact (`backend/Admin.WebApi/Program.cs`), wired to ServiceMantle with #33.

- **Enabled capabilities**: `AddServiceMantle(ServiceId.Parse("ruoyu-admin"), InstanceId.Parse(...))` registers the service identity and the request log scope; `AddOpenTelemetryInstrumentation()` enables the ASP.NET Core / HttpClient / Runtime instrumentations with default options (no exporter at all — traces/metrics never leave the process). `UseServiceMantleCorrelationId()` is the pipeline's **first middleware** (before `UseCors`), so CORS, static files, the SPA fallback, authentication, the three proxies, and every `/api` response get the `x-correlation-id` response header (injected via `OnStarting`, covering static file responses too).
- **Service identity**: `ServiceId` is fixed to `ruoyu-admin` (a stable deployment-level identifier, deliberately distinct from the Serilog display name `Ruoyu.Admin`). `ServiceVersion` is not passed explicitly; it resolves from the entry assembly's informational version (currently 1.0.0).
- **Instance identity and deployment semantics**: `InstanceId` looks like `ruoyu-admin-<32 lowercase hex>`, **newly generated per host build and changed on restart — not a persistent identity**; it must not be used for cross-restart correlation or as an audit subject.
- **Correlation ID rules**: an inbound `x-correlation-id` is echoed verbatim only when it is "single-value and well-formed" (≤64 chars, ASCII alphanumerics plus `.`/`_`/`-`, first char alphanumeric); missing, overlong, illegal characters, comma-joined, or duplicated headers are dropped entirely and a fresh 32-char lowercase hex value is generated; request headers are never rewritten.
- **Log field interaction**: the existing Serilog enricher order (`FromLogContext` first, global `WithProperty("ServiceName"/"ServiceVersion"/"InstanceId")` last) is unchanged; global fields keep overriding same-named request-scope fields, Loki field values and the existing query contract stay unchanged; the new `CorrelationId` property reaches Console and Loki output via `FromLogContext`. Unified identity field ownership is in #35.
- **Health endpoints** (added with #34; switched to the shared EF Core snapshot in #66): `AddServiceMantleHealthEndpoints(options => options.ProbeTimeout = 3s)` registers the health services, and the endpoints are mapped by `app.MapGroup(string.Empty).AllowAnonymous()` + `MapServiceMantleHealthEndpoints()` as `GET /health/live` (always 200 `{"status":"live"}`), `GET /health/ready`, and `GET /health` (readiness alias). The shared `StartupDatabaseGate` runs the one-and-only preparation and migration before WebHost/worker startup; while the shared singleton `StartupDatabaseReceipt` is NotStarted/Running there is zero database access and 503 `ruoyu-admin.startup_incomplete`; when Failed, zero database access and 503 `ruoyu-admin.startup_failed`. After Succeeded, the scoped `EfCoreHealthSnapshotSource<AuditDbContext>` reads the EF-mapped table columns with a zero-row `SELECT`; it proves only that table columns are readable, not constraints, indexes, or data correctness. The PostgreSQL classifier distinguishes connection failure `ruoyu-admin.database_unreachable` from schema faults `ruoyu-admin.schema_unavailable`; unknown exceptions/timeouts still get the library's safe answers `health.probe_failed`/`health.probe_timeout`. Re-sampled per request; downstreams deliberately do not participate in readiness, and responses contain no connection strings/hosts/exception text. The anonymous route group and the SPA's `/health` exclusion predicate are preserved, and `/api/*` still requires authentication. For the full gate and compatibility notes see the [migration documentation](../database/migrations.md#66-release-compatibility-notes), and for the probing conventions see the [deployment documentation](../development/Deployment.md).
- **Bootstrap boundary**: no `bootstrapFilePath` is passed and no Bootstrap database provider, installation, or management capability is registered; startup writes zero bytes to disk.
- **Security response header baseline** (added with #55): `AddSecurityResponseHeaders()` registers the services + `UseServiceMantleSecurityResponseHeaders()` (after `UseCors`, before the static-files/SPA/session middleware; WebApplication implicit routing runs first, so the middleware position is arbitrary) + `[RequireSecurityResponseHeaders]`/`AdminSecurityResponseHeadersConvention` marking the 10 JSON controllers (AdminAuth, AdminCsrf, AdminOidc, EnumOptions, IdentityAccounts, Mistake, OssAudit, OssUploadRecord, Students, StudentAssociations); the SignaCore hosted-login package endpoints (`/api/auth/oidc/*`) are deliberately unmarked (owned by the package since #83; their answers carry their own no-store). Every routed response of a marked endpoint whose headers have not started (success, business 4xx, 401/403 challenge, 5xx, session-boundary/logout middleware short-circuits) carries the six fixed single-value headers: `Cache-Control: no-store`, `Pragma: no-cache`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and the CSP `default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`. The former hand-written headers (AdminCsrfController, AdminSessionBoundary.RejectAsync) now use the baseline rules uniformly; the old marker-only routes and the logout middleware were removed with #83's package replacement. Deliberately unmarked: `ImageController` (browser-native image redirects), the three proxy middlewares' forwarded responses, the SPA/static files, and the health endpoints.
- **Exporter wiring boundary**: wiring the OTLP / Prometheus exporters is left to a later task (#35 shared logging migration extends per its own issue). Proxied outbound requests add the standard W3C `traceparent`/`tracestate` propagation headers (default HttpClient instrumentation behavior); downstreams ignore them as unknown headers.

## Integration matrix

| # | Direction | Type | Target service | Protocol | Address configuration | Purpose | APIs/methods used |
|---|------|------|----------|------|----------|------|----------------|
| 1 | Outbound | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (default `:5005`) | Student management | `StudentLearningGrpcService`: ListStudents, GetStudent, CreateStudent, UpdateStudent, DeleteStudent, GetIdentityAccountsByStudentId, LinkIdentityAccountToStudent, UnlinkIdentityAccountFromStudent, GetStudentOpenSubjects, SetStudentOpenSubjects, GetAvailableSubjects |
| 2 | Outbound | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (default `:5005`) | Learning/upload records | `StudentLearningGrpcService`: GetUploadRecord, MarkUploadRecordUnderReview, RotateUploadImage, RemoveImageFromRecord, RemoveImagesFromRecord, DeleteUploadRecordAfterReview, ResetUploadRecordStatus |
| 2a | Outbound | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (default `:5005`) | Admin upload records (to be migrated) | `StudentManagementGrpcService`: GetAllUploadRecords, GetStudent, AnalyzeUploadRecord (to be migrated to the generic interfaces in Task 4.6) |
| 3 | Outbound | gRPC Client | Mistake Service | gRPC (h2c) | `MistakeGrpcService:Address` (default `:5006`) | Mistake management | `MistakeGrpcService`: GetMistakeItemList, GetMistakeItem, GetMistakeItemsByUpload, UpdateMistakeItem, SubmitMistakeUpload, CompleteUploadReview |
| 4 | Outbound | HTTP Proxy | Identity Service | HTTP/JSON | `IdentityService:Authority` (default `:5002`) | Authentication proxy | `IdentityProxyMiddleware`: `/api/identity/*` → `{Authority}/api/*` |
| 5 | Outbound | HTTP Client | Identity Service | HTTP/JSON | `IdentityService:Authority` | Batch account lookup | `POST {Authority}/api/gateway/users/batch` (with `X-Admin-AppId` + `X-Admin-AppSecret`) |
| 6 | Outbound | HTTP Proxy | Teacher Portal | HTTP/JSON | `TeacherPortal:Url` (default `:5004`) | Teacher-side proxy | `TeacherPortalProxyMiddleware`: `/api/teacher-portal/*` → `{Url}/api/*` (sends the in-ticket server access token, rejects inbound Authorization; downstream checks `[Authorize(Roles="admin")]`) |
| 6a | Outbound | HTTP Proxy | Assistant Portal | HTTP/JSON | `AssistantPortal:Url` (default `:5021`) | Assistant-side proxy | `AssistantPortalProxyMiddleware`: `/api/assistant-portal/*` → `{Url}/api/*` (sends the in-ticket server access token, rejects inbound Authorization; downstream checks `[Authorize(Roles="admin")]`) |
| 7 | Outbound | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (default `:5005`) | Image presigned URLs | `StudentLearningGrpcService`: GetPresignedUrl |
| 8 | Outbound | gRPC Client | Mistake Service | gRPC (h2c) | `MistakeGrpcService:Address` (default `:5006`) | Image presigned URLs | `MistakeGrpcService`: GetPresignedUrl |
| 9 | Outbound | gRPC Client | Student Service | gRPC (h2c) | `StudentGrpcService:Address` (default `:5005`) | Image migration | `StudentLearningGrpcService`: MigrateImagesToMistake |
| 10 | Outbound | S3 API | OSS storage | S3 / local files | `Oss:*` configuration | Audit browsing + zombie cleanup + migration assistance | `IOssService`: ListObjectsAsync, DeleteAsync (automatically cleans associated thumbnails), CopyObjectAsync, GetPresignedUrl, ObjectExists; no path-prefix validation (the audit needs access to all path prefixes) |
| 11 | Inbound | HTTP | Frontend Web UI | HTTP/JSON | `AdminApi:Port` (default `:5020`) | Management API | All `/api/admin/*` routes |

## Failure semantics summary

### Downstream call failures

> The actual protocol is HTTP/JSON; the section title keeps the original wording to preserve links; the Storage Audit rows in the table below have been corrected per the current code.

| Scenario | Service | Failure handling | HTTP response |
|------|------|----------|-----------|
| Student Service unavailable | Student | the audit job is marked Failed (path aggregation unavailable); Resolve / BatchResolve return 502 and delete nothing; CRUD operations return 500; image presigned URLs return 502; image migration returns 500 | 502 Bad Gateway / 500 Internal Server Error |
| Homework Service unavailable | Homework | the audit job is marked Failed (`ErrorMessage="Homework service unavailable"`), aborting before scanning any bucket; Resolve / BatchResolve return 502 and delete nothing (the re-checking `GetRegisteredOssPathsAsync` aggregates both Student and Homework) | 502 Bad Gateway / 500 Internal Server Error |
| Mistake Service unavailable | Mistake | the audit job is marked Failed (reference aggregation incomplete and forbidden to yield disposable conclusions); Resolve / BatchResolve return 502 and the whole batch deletes nothing; queries return 500; image presigned URLs return 502 | 502 Bad Gateway / 500 Internal Server Error |
| gRPC InvalidArgument | Student/Mistake | returns a client error | 400 Bad Request |
| gRPC NotFound | Student/Mistake | returns resource not found | 404 Not Found |
| gRPC generic exception | Student/Mistake | logs and returns 500 | 500 Internal Server Error |

### HTTP proxy failures

| Scenario | Service | Failure handling | HTTP response |
|------|------|----------|-----------|
| Identity Service unreachable | Identity | returns 502 | `{"message":"Identity service unreachable"}` |
| Teacher Portal unreachable | Teacher Portal | returns 502 | `{"message":"Teacher portal service unreachable"}` |
| Teacher Portal unconfigured | Teacher Portal | returns 503 | `{"message":"Teacher portal not configured"}` |
| Assistant Portal unreachable | Assistant Portal | returns 502 | `{"message":"Assistant portal service unreachable"}` |
| Assistant Portal unconfigured | Assistant Portal | returns 503 | `{"message":"Assistant portal not configured"}` |
| Identity batch lookup failure | Identity | logs a warning and returns an empty list | 200 OK + `[]` |

### OSS operation failures

| Scenario | Failure handling | Impact |
|------|----------|------|
| OSS ListObjects failure | the audit job is marked Failed | the audit cannot complete |
| OSS DeleteObject failure (audit cleanup) | returns 500 | the audit record is marked Resolved but the OSS file was not deleted |
| OSS DeleteObject failure (thumbnail cleanup) | the error is ignored and the original image keeps being deleted | thumbnails may remain, but the original deletion is unaffected |
| OSS CopyObject failure (migration assistance) | returns 500 | the OSS object move is incomplete |
| gRPC GetPresignedUrl failure | returns 502 Bad Gateway | the image cannot be previewed |
| gRPC MigrateImagesToMistake failure | returns 500 | the image migration is incomplete |

### Database operation failures

| Scenario | Failure handling | Impact |
|------|----------|------|
| Concurrent audit run writes | DbUpdateException → exits the current audit | guarantees only one audit run at a time |
| Delete fails after an audit record Resolve | returns 500 | the OSS file is deleted but the audit record is not cleaned up [inferred] |

## Retry policy

The current implementation has **no** automatic retry mechanism:

- gRPC calls: no retry; failures return errors directly
- HTTP proxies: no retry; failures return 502 directly
- OSS operations: no retry; failures return errors directly
- The audit scheduled job: after a failure it waits for the next scheduled trigger (once a day) or a manual trigger

## Timeout configuration

| Connection | Timeout | Configuration |
|------|------|----------|
| Identity HTTP Client | 30 seconds | `HttpClient.Timeout` |
| Teacher Portal HTTP Client | 10 seconds | `HttpClient.Timeout` |
| Assistant Portal HTTP Client | 10 seconds | `HttpClient.Timeout` |
| gRPC Client | default [to confirm] | `AddGrpcClient` defaults |
| OSS operations | default [to confirm] | decided by the `IOssService` implementation |
