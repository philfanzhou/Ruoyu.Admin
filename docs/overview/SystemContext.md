# System context

## Service positioning

Admin Portal is the **administration console service** of the Ruoyu.Study platform, giving administrators a unified entry point for student management, mistake management, OSS audit, and upload record management. It holds no core business data of its own; it acts as an aggregation layer proxying downstream microservices and calls them in operation order.

**Core responsibilities**:
- Student CRUD and identity account linking management
- Mistake record query, update, and image migration
- OSS zombie object audit (scheduled + manual triggers)
- Upload record viewing, assignment, rotation, removal, legacy data cleanup
- HTTP reverse proxying for the identity service and the teacher portal
- Enum options aggregation query

## System context diagram

```
                    ┌─────────────────────────┐
                    │     Admin                │
                    │  accesses the console    │
                    │  via a browser           │
                    └───────────┬─────────────┘
                                │ HTTPS
                                ▼
                    ┌─────────────────────────┐
                    │   Nginx container        │
                    │   frontend static files  │
                    │   + reverse proxy        │
                    └───────────┬─────────────┘
                                │ HTTP/JSON
                                ▼
┌───────────────────────────────────────────────────────────┐
│                    Admin Portal API                        │
│                  (ASP.NET Core 8.0)                        │
│                   Port: 5020                               │
│                                                           │
│  ┌─────────────┐  ┌──────────────┐  ┌─────────────────┐  │
│  │ Controllers │  │  Background  │  │   Middlewares   │  │
│  │  (REST API) │  │   Services   │  │  (Proxy)        │  │
│  └──────┬──────┘  └──────┬───────┘  └────────┬────────┘  │
│         │                │                    │           │
│  ┌──────┴────────────────┴────────────────────┴────────┐  │
│  │              AuditDbContext (EF Core)                │  │
│  │           OssAuditRecords / OssAuditRuns            │  │
│  └──────────────────────┬──────────────────────────────┘  │
│                         │                                  │
│  ┌──────────────────────┴──────────────────────────────┐  │
│  │              IOssService (S3 / Local)                │  │
│  │      [audit List + cleanup Delete only]             │  │
│  └─────────────────────────────────────────────────────┘  │
└───────────┬──────────────────┬──────────────┬─────────────┘
            │ gRPC             │ gRPC         │ HTTP Proxy
            │ :5005            │ :5006        │
            ▼                  ▼              ▼
   ┌────────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
   │ Student Service│ │Mistake Service│ │Identity Svc  │ │Teacher Portal│
   │ (gRPC :5005)  │ │(gRPC :5006)  │ │(HTTP :5002)  │ │(HTTP :5004)  │
   └────────────────┘ └──────────────┘ └──────────────┘ └──────────────┘
```

## Upstream (callers)

### Administrator browser → Nginx container

| Property | Value |
|------|------|
| Caller | administrator browser |
| Protocol | HTTPS |
| Description | the administrator accesses the frontend through a browser; the frontend container or the integrated ASP.NET Core host serves the static files and the API entry |

### Nginx container → Admin Portal API

| Property | Value |
|------|------|
| Caller | Nginx container |
| Protocol | HTTP/JSON |
| Route prefix | `/api/admin/*` |
| Description | Nginx proxies API requests to the Admin Portal backend |

### Administrator browser → public OSS entry

| Property | Value |
|------|------|
| Caller | administrator browser |
| Protocol | HTTPS |
| Route prefix | `/oss/*` |
| Public entry | `https://oss.example.com/oss/*` |
| Description | after the Admin API returns a 302 presigned address, the browser visits the platform's public OSS entry; in this repository it is uniformly proxied by the User Web Nginx — the Admin Nginx no longer maintains an `/oss/` location |

## Downstream (callees)

### Student Service (gRPC)

| Property | Value |
|------|------|
| Service names | StudentManagementGrpcService / StudentLearningGrpcService |
| Protocol | gRPC (HTTP/2, h2c) |
| Default address | `http://localhost:5005` |
| Configuration key | `StudentGrpcService:Address` |
| gRPC methods used | `ListStudents`, `GetStudent`, `CreateStudent`, `UpdateStudent`, `DeleteStudent`, `GetIdentityAccountsByStudentId`, `LinkIdentityAccountToStudent`, `UnlinkIdentityAccountFromStudent`, `GetStudentOpenSubjects`, `SetStudentOpenSubjects`, `GetAvailableSubjects`, `GetRegisteredOssPaths`, `ListOssObjects`, `DeleteOssObject`, `GetAllUploadRecords`, `GetUploadRecord`, `MarkUploadRecordCompleted`, `RotateUploadImage`, `RemoveImageFromRecord`, `RemoveImagesFromRecord`, `DeleteUploadRecordAfterReview`, `AnalyzeUploadRecord`, `GetPresignedUrl`, `MigrateImagesToMistake` |
| Behavior when unavailable | gRPC `StatusCode.Unavailable` → returns 500/502; the audit job aborts (marked Failed when the Student service is unavailable) |

### Mistake Service (gRPC)

| Property | Value |
|------|------|
| Service name | MistakeGrpcService |
| Protocol | gRPC (HTTP/2, h2c) |
| Default address | `http://localhost:5006` |
| Configuration key | `MistakeGrpcService:Address` |
| gRPC methods used | `GetMistakeItemList`, `GetMistakeItem`, `GetMistakeItemsByUpload`, `UpdateMistakeItem`, `GetAllReferencedImagePaths`, `SubmitMistakeUpload`, `CompleteUploadReview`, `GetPresignedUrl` |
| Behavior when unavailable | the audit job continues but may produce false positives; Resolve returns 502; queries return 500 |

### Identity Service (HTTP)

| Property | Value |
|------|------|
| Protocol | HTTP/JSON (reverse proxy) |
| Default address | `http://localhost:5002` |
| Configuration keys | `IdentityService:Authority`, `IdentityService:AppId`, `IdentityService:AppSecret` |
| Proxy middleware | `IdentityProxyMiddleware` |
| Route mapping | `/api/identity/*` → `{Authority}/api/*` |
| Authentication | headers `X-Admin-AppId` + `X-Admin-AppSecret` |
| Timeout | 30 seconds |
| Behavior when unavailable | returns 502 + `{"message":"Identity service unreachable"}` |

### Teacher Portal (HTTP)

| Property | Value |
|------|------|
| Protocol | HTTP/JSON (reverse proxy) |
| Default address | `http://localhost:5004` |
| Configuration key | `TeacherPortal:Url` |
| Proxy middleware | `TeacherPortalProxyMiddleware` |
| Route mapping | `/api/teacher-portal/admin/*` → `{Url}/api/admin/*` |
|  | `/api/teacher-portal/auth/*` → `{Url}/api/auth/*` |
|  | `/api/teacher-portal/*` → `{Url}/api/admin/*` |
| Authentication | sends the in-ticket server access token and rejects inbound Authorization (downstream checks `[Authorize(Roles="admin")]`) |
| Timeout | 10 seconds |
| Behavior when unconfigured | returns 503 + `{"message":"Teacher portal not configured"}` |
| Behavior when unavailable | returns 502 + `{"message":"Teacher portal service unreachable"}` |

### OSS storage

| Property | Value |
|------|------|
| Protocol | S3-compatible API / local file system |
| Configuration keys | `Oss:InternalEndpoint`, `Oss:InternalSecure`, `Oss:AccessKey`, `Oss:SecretKey`, `Oss:BucketName`, `Oss:PublicBaseUrl` |
| Environment switch | `USE_LOCAL_OSS=1` → uses `LocalFileOssService` |
| Usage scenarios | audit ListObjects, cleanup DeleteObject (permissions downgraded to read-only + limited write) |
| Public access | `Oss:PublicBaseUrl` controls the presigned public address; the User Web Nginx handles the `/oss/` proxy |

## Service boundaries

Admin Portal's service boundaries are clearly defined as follows:

| Responsibility | Owner | Description |
|------|------|------|
| Student data storage | Student Service | Admin Portal only proxies CRUD and stores no student data |
| Mistake data storage | Mistake Service | Admin Portal only proxies query/update and stores no mistake data |
| Authentication data | Identity Service | Admin Portal only proxies requests and stores no account data |
| Teacher-side data | Teacher Portal | Admin Portal only proxies requests and stores no teacher-side data |
| OSS audit data | Admin Portal (local) | audit records and run records are managed by Admin Portal's own database |
| OSS object management | Student Service + OSS | Admin Portal operates OSS indirectly through the Student Service's gRPC methods |
| Image presigned URLs | Student/Mistake Service | Admin Portal obtains presigned URLs via gRPC GetPresignedUrl and issues a 302 redirect |
| Image migration | Student Service | Admin Portal proxies migration through Student gRPC MigrateImagesToMistake |
