# Run and debug (RunAndDebug)

## Start the backend

```bash
cd backend

# Development mode (with Swagger UI)
dotnet run --project Admin.WebApi/Admin.WebApi.csproj
```

`backend/` holds a multi-project solution; `dotnet run` must name the project explicitly.

The listen port **5020 is the `const int httpPort` in `Program.cs`, hardcoded and not configurable**.

Startup precondition: `IdentityService:AppId` and `IdentityService:AppSecret` must be configured, otherwise the host throws `InvalidOperationException` during startup. For local development, inject them via environment variables:

```bash
IdentityService__AppId=dev-app-id \
IdentityService__AppSecret=dev-app-secret \
dotnet run --project Admin.WebApi/Admin.WebApi.csproj
```

After startup, visit:
- API: `http://localhost:5020/`
- Swagger UI: `http://localhost:5020/swagger` (Development environment only)
- Health check: `curl http://localhost:5020/` → returns "Student Admin WebAPI is running."

## Start the frontend

```bash
cd frontend

# Development mode (hot reload)
npm run dev

# Build the production bundle
npm run build
```

The dev server listens on 8090 and proxies every `/api/*` request (including `/api/auth/*` used by login/logout) to `http://localhost:5020`.

## Integrated deployment mode

Deploy the frontend build output into the backend wwwroot:

```bash
cd frontend
npm run build
mkdir -p ../backend/Admin.WebApi/wwwroot
cp -r dist/* ../backend/Admin.WebApi/wwwroot/
```

Once the backend starts, it serves the frontend static files and the SPA route fallback automatically. `wwwroot/` is excluded by both `.gitignore` and `.dockerignore`; during Docker builds the frontend-build stage of `backend/Admin.WebApi/Dockerfile` injects it directly, so manual copying is not needed.

## Debugging tips

### Degraded testing with downstream services unavailable

- Setting `USE_LOCAL_OSS=1` swaps `IOssService` for a local-directory implementation (`OSS_LOCAL_PATH`, default `data/oss`), letting you debug Storage Audit without an S3-compatible store.
- `OssAuditController`'s `GET records` / `GET status` / `POST records/{id}/ignore` depend only on the local `ruoyu_admin` database and can be debugged standalone; `POST trigger` needs every downstream and OSS reachable, and `POST records/{id}/resolve` and `POST records/batch-resolve` would **actually delete files in object storage**.
- `StudentsController`, `MistakeController`, `ImageController`, `StudentAssociationsController`, and `OssUploadRecordController` all need the corresponding Student (:5005) / Mistake (:5007) HTTP services actually reachable; otherwise they return 502.
- `HomeworkReferenceClient` needs Homework (:5009) reachable; when it is not, the audit run is marked Failed rather than skipped.
- The three proxy middleware return 502 when a downstream is unreachable and 503 when the address is unconfigured.

### Adjusting log levels

The real Console/Loki pipeline comes from `AdminLoggingExtensions`' `ServiceMantle.Logging` settings: an Information global floor plus two Warning overrides: `Microsoft.AspNetCore` and `Microsoft.EntityFrameworkCore.Database.Command`. They share the filtering and the forced structured masking; the old `Serilog` section has been removed.

The `appsettings.Development.json` below overrides only the framework's `Logging:LogLevel` pre-filter; lowering it cannot break through the pipeline floor above, while raising it can suppress logs further. To lower the real output floor you must also change the corresponding ServiceMantle options in code:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  }
}
```
