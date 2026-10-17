# Verification and testing (Verification)

## Run the tests

```bash
cd backend

# Run all tests (integration tests need local Docker, see below)
dotnet test Ruoyu.Admin.sln --configuration Release

# Run a specific test class
dotnet test Ruoyu.Admin.sln --filter "FullyQualifiedName~OssUploadRecordControllerLegacyTests"

# Run the model tests
dotnet test Ruoyu.Admin.sln --filter "FullyQualifiedName~ModelTests"
```

> **Docker prerequisite**: the integration tests under `Tests/Integration/` (ServiceMantle host startup, correlation propagation) start a one-off PostgreSQL container through Testcontainers and need local Docker (CI's ubuntu-latest has it). Without Docker you can run the remaining suites:
>
> ```bash
> dotnet test Ruoyu.Admin.sln --filter "FullyQualifiedName!~Admin.WebApi.Tests.Integration"
> ```
>
> Integration tests live in a dedicated xunit collection (`service-mantle-integration`), parallel with other test classes and serial within the collection.

## Test project structure

```
test/Admin.WebApi.Tests/
├── Admin.WebApi.Tests.csproj
├── Integration/
│   ├── ServiceMantleIntegration.cs              # Testcontainers PostgreSQL fixture and shared base class
│   ├── ServiceMantleHostStartupTests.cs         # Host startup / service identity / anonymous SPA contract / 401
│   └── ServiceMantleCorrelationTests.cs         # x-correlation-id echo and safety rules / log scopes
├── Models/
│   └── ModelTests.cs              # DTO constructor tests
├── Controllers/
│   ├── AssignmentControllerTests.cs
│   ├── EnumOptionsControllerTests.cs          # Enum options aggregation tests
│   ├── IdentityAccountsControllerTests.cs
│   ├── ImageControllerTests.cs                # Image proxy path validation and gRPC exception tests
│   ├── MistakeControllerTests.cs
│   ├── OssAuditControllerTests.cs
│   ├── OssUploadRecordControllerLegacyTests.cs
│   ├── OssUploadRecordControllerTests.cs
│   └── StudentsControllerTests.cs
├── Middleware/
│   ├── IdentityProxyMiddlewareTests.cs
│   └── TeacherPortalProxyMiddlewareTests.cs
└── Services/
    └── OssAuditWorkerTests.cs
```

## Coverage

```bash
dotnet test --collect:"XPlat Code Coverage"
```

> No coverage target is configured today. Suggested target: controller layer >= 80%.

## Build verification

```bash
# Release build
cd backend
dotnet publish -c Release -o ./publish

# Verify the build output
ls ./publish/Admin.WebApi.dll
```

## API verification

With the backend running, verify through the Swagger UI or curl:

```bash
# Health check
curl http://localhost:5020/

# Enum options (no downstream services needed)
curl http://localhost:5020/api/admin/enum-options

# Audit status (local database only)
curl http://localhost:5020/api/admin/oss-audit/status
```

## Current test coverage

| Module | Test file | Coverage |
|------|----------|----------|
| Models (DTO) | ModelTests.cs | DTO constructors |
| OssUploadRecordController (Legacy) | OssUploadRecordControllerLegacyTests.cs | LegacyCheck/LegacyClean |
| OssUploadRecordController | OssUploadRecordControllerTests.cs, AssignmentControllerTests.cs | Assign/rotate/reset/list/detail |
| StudentsController | StudentsControllerTests.cs | ListStudents/CreateStudent/GetStudent |
| MistakeController | MistakeControllerTests.cs | GetMistakeItems/UpdateMistakeItem |
| OssAuditController | OssAuditControllerTests.cs | Audit record management |
| OssAuditWorker | OssAuditWorkerTests.cs | Background audit jobs |
| IdentityProxyMiddleware | IdentityProxyMiddlewareTests.cs | Proxy forwarding |
| TeacherPortalProxyMiddleware | TeacherPortalProxyMiddlewareTests.cs | Proxy forwarding |
| IdentityAccountsController | IdentityAccountsControllerTests.cs | Account batch lookup / linked students |
| EnumOptionsController | EnumOptionsControllerTests.cs | GetAll aggregates 5 enum lists |
| ImageController | ImageControllerTests.cs | Path validation (empty/invalid/normal), mistakes paths going to Mistake gRPC, others to Student gRPC, NotFound/other gRPC exceptions |

### EnumOptionsController test essentials

- `GetAll` returns an `EnumOptionsResponse` containing 5 lists: UploadStatuses, Grades, Subjects, Classifications, ReviewStatuses
- Each list's element count should match the corresponding `*Constants.EnglishNames`
- Each element's `Value`/`Name`/`DisplayName` fields should map correctly

### ImageController test essentials

- Path validation: empty path → 400; containing `..` or starting with `/` or `\` → 400
- Path starting with `mistakes/` (case-insensitive) → calls `MistakeGrpcService.GetPresignedUrlAsync`
- Any other path → calls `StudentLearningGrpcService.GetPresignedUrlAsync`, passing the `size` parameter
- gRPC success → 302 redirect to the presigned URL
- gRPC throws `NotFound` → 404
- gRPC throws another exception → 502

> For detailed test plans see each module's 05-TESTS.md
