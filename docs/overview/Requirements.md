# Requirements document

## Service-level requirements

Admin Portal is the administration console service of the Ruoyu.Study platform, giving administrators a unified entry point for student management, mistake management, OSS audit, and upload record management.

### Functional requirements

| # | Requirement | Priority | Status | Module |
|---|------|--------|------|----------|
| FR-1 | Student CRUD management | High | Implemented | StudentsController |
| FR-2 | Student and identity account linking/unlinking | High | Implemented | StudentsController |
| FR-3 | Student open-subject management | Medium | Implemented | StudentsController |
| FR-4 | Mistake record query and update | High | Implemented | MistakeController |
| FR-5 | Mistake image migration (uploads → mistakes) | Medium | Implemented | MistakeController |
| FR-6 | OSS zombie object audit (scheduled + manual) | High | Implemented | OssAuditController + OssAuditWorker |
| FR-7 | OSS audit record management (Resolve/Ignore/Batch) | High | Implemented | OssAuditController |
| FR-8 | Upload record viewing and assignment | High | Implemented | OssUploadRecordController |
| FR-9 | Upload record image operations (rotate/remove) | Medium | Implemented | OssUploadRecordController |
| FR-10 | Legacy data detection and cleanup | Medium | Implemented | OssUploadRecordController |
| FR-11 | Upload record VL analysis | Low | Implemented | OssUploadRecordController |
| FR-12 | Identity service reverse proxy | High | Implemented | IdentityProxyMiddleware |
| FR-13 | Teacher portal reverse proxy | Medium | Implemented | TeacherPortalProxyMiddleware |
| FR-14 | Enum options aggregation query | Low | Implemented | EnumOptionsController |
| FR-15 | Image proxy (from OSS) | Medium | Implemented | ImageController / OssUploadRecordController |
| FR-16 | Identity account batch lookup | Medium | Implemented | IdentityAccountsController |

### Non-functional requirements

| # | Requirement | Description |
|------|------|------|
| NFR-1 | Audit concurrency safety | Only one audit run at a time, mutual exclusion via database records |
| NFR-2 | Audit idempotency | Audit DDL uses `IF NOT EXISTS`; audit records avoid duplication via the `ObjectPath` UNIQUE constraint |
| NFR-3 | Downstream fault tolerance | The audit aborts when the Student service is unavailable; it continues with a warning when the Mistake service is unavailable |
| NFR-4 | Proxy fault tolerance | Returns 502 without crashing when Identity/Teacher Portal is unreachable |
| NFR-5 | Image path safety | ImageController checks paths for `..` or a leading `/` or `\` to prevent path traversal attacks |
| NFR-6 | CORS configuration | Production restricts cross-origin sources through `AdminWeb:AllowedOrigins` |
| NFR-7 | Database | Supports PostgreSQL via the Npgsql EF Core provider |
| NFR-8 | SPA integration | Supports both integrated frontend/backend deployment (wwwroot) and standalone deployment |

### Constraints

| # | Constraint | Description |
|------|------|------|
| C-1 | Holds no core business data | Student, mistake, and identity data are queried live through downstream services only |
| C-2 | No direct writes to external databases | All external data modifications go through gRPC/HTTP interfaces |
| C-3 | Audit only detects, never auto-deletes | The audit only marks zombie objects; an administrator must Resolve manually before deletion |
| C-4 | References must be re-verified before Resolve | Before deleting an OSS object, the Student/Mistake services must be re-checked for references to the path |
