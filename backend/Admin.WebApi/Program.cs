using System.Data.Common;
using Admin.WebApi;
using Admin.WebApi.Models;
using Admin.WebApi.Persistence;
using Admin.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Ruoyu.Admin.Common.Authentication;
using Ruoyu.Admin.Common.Database;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.Consul;
using Ruoyu.Admin.ServiceClients;
using ServiceMantle;

var builder = WebApplication.CreateBuilder(args);

// ========== Consul Configuration Source ==========
builder.Configuration.AddRuoyuConsulConfiguration(builder.Configuration);
var consulOptions = RuoyuConsulOptions.Bind(builder.Configuration);
var consulRuntimeState = RuoyuConsulRuntimeState.Instance;

// ========== Serilog (Console + Grafana Loki) ==========
builder.Configuration.AddRuoyuLokiSink();
builder.Host.UseRuoyuSerilog("Ruoyu.Admin");

// HTTP listen port is hardcoded to 5020 (not configurable).
// nginx in the same container proxies /api/ to this port.
const int httpPort = 5020;

var studentServiceUrl = builder.Configuration["StudentService:Url"] ?? "http://localhost:5005";
var mistakeServiceUrl = builder.Configuration["MistakeService:Url"] ?? "http://localhost:5007";
var homeworkServiceUrl = builder.Configuration["HomeworkService:Url"] ?? "http://localhost:5009";
var identityAppId = builder.Configuration["IdentityService:AppId"];
var identityAppSecret = builder.Configuration["IdentityService:AppSecret"];
if (string.IsNullOrWhiteSpace(identityAppId) || string.IsNullOrWhiteSpace(identityAppSecret))
{
    throw new InvalidOperationException(
        "IdentityService:AppId and IdentityService:AppSecret must be configured for Admin Portal.");
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(httpPort);
});

// Configure downstream clients
// Student service: HTTP (migrated from gRPC)
builder.Services.AddStudentHttpClient(studentServiceUrl);

// Mistake service: HTTP (migrated from gRPC)
builder.Services.AddMistakeHttpClient(mistakeServiceUrl);
builder.Services.AddHttpClient<HomeworkReferenceClient>(client =>
{
    client.BaseAddress = new Uri(homeworkServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

// ========== Authentication (JWT Bearer via Identity OIDC) ==========
// Admin portal authenticates via Identity-issued JWT. The admin role is injected by Identity
// through admin_portal's callback (/api/auth/callback) for whitelisted AdminUserIds.
builder.Services.AddRuoyuJwtBearer(
    builder.Configuration,
    builder.Environment,
    options =>
    {
        // Browser-native image requests cannot attach an Authorization header.
        // The shared handler preserves header precedence and uses this cookie only as fallback.
        options.AccessTokenCookieName = "adminAuthToken";
    });

builder.Services.Configure<IdentityServiceOptions>(
    builder.Configuration.GetSection(IdentityServiceOptions.SectionName));

builder.Services.Configure<TeacherPortalOptions>(
    builder.Configuration.GetSection(TeacherPortalOptions.SectionName));

builder.Services.Configure<AssistantPortalOptions>(
    builder.Configuration.GetSection(AssistantPortalOptions.SectionName));

builder.Services.Configure<AdminPortalOptions>(
    builder.Configuration.GetSection(AdminPortalOptions.SectionName));

builder.Services.AddHttpClient("IdentityService", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("TeacherPortal", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient("AssistantPortal", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetSection("AdminWeb:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    options.AddPolicy("AdminWeb", policy =>
    {
        if (origins.Length == 0)
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            return;
        }
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddControllers();

// ========== ServiceMantle (service identity, correlation id, base telemetry, health) ==========
// ServiceId "ruoyu-admin" is the stable deployment identity (lowercase; deliberately distinct
// from the Serilog display name "Ruoyu.Admin"). The InstanceId is regenerated on every host
// build and is NOT a persistent identity: it changes on each restart. No bootstrapFilePath is
// passed: the bootstrap store stays a lazy singleton and this wiring performs zero disk writes.
// No serviceVersion is passed: it resolves from the entry assembly informational version.
// AddOpenTelemetryInstrumentation uses the default options (AspNetCore / HttpClient / Runtime
// instrumentation) and registers NO exporter.
// AddServiceMantleHealthEndpoints registers the health endpoint services ONLY (the actual
// /health/live, /health/ready and /health routes are mapped below). No IServiceHealthSnapshotSource
// and no readiness contributor is registered: readiness is honestly fail-closed (503
// health.probe_failed) until Admin gains a real readiness evidence source.
builder.Services
    .AddServiceMantle(
        ServiceId.Parse("ruoyu-admin"),
        InstanceId.Parse($"ruoyu-admin-{Guid.NewGuid():N}"))
    .AddOpenTelemetryInstrumentation()
    .AddServiceMantleHealthEndpoints();

// IOssService: 保留用于 OSS 审计和运维操作
// 权限范围：只读（ListObjects, Download, GetPresignedUrl, ObjectExists）+ 有限写（Delete 僵尸清理, CopyObject 迁移辅助）
// 理想状态：后续将 Delete/CopyObject 也走 gRPC，Admin 可完全去除写凭证
var useLocalOss = Environment.GetEnvironmentVariable("USE_LOCAL_OSS") == "1";
builder.Services.AddSingleton<IOssService>(sp =>
{
    if (useLocalOss)
    {
        var localPath = Environment.GetEnvironmentVariable("OSS_LOCAL_PATH") ?? "data/oss";
        return new LocalFileOssService(localPath);
    }
    var ossOptions = builder.Configuration.GetSection("Oss").Get<OssOptions>() ?? new OssOptions();
    return new S3OssService(ossOptions);
    // 不配置 allowedPrefixes：Admin Portal 需要访问所有路径前缀（审计+运维）
});

var connectionString = SharedPostgreSqlConnectionStringFactory.BuildOrFallback(
    builder.Configuration,
    builder.Configuration.GetConnectionString("AuditDb"));

builder.Services.AddDbContext<AuditDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddSingleton<OssAuditWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OssAuditWorker>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Admin Portal API", Version = "v1" });
});

var app = builder.Build();
var identityTrust = app.Services
    .GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityAuthenticationOptions>>()
    .Value;

app.Logger.LogInformation("Admin Portal starting");
app.Logger.LogInformation(
    "Identity trust: Authority={Authority}, Issuers={Issuers}, Audience={Audience}, RequireHttpsMetadata={RequireHttpsMetadata}",
    identityTrust.Authority,
    string.Join(",", identityTrust.GetValidIssuers()),
    identityTrust.Audience,
    identityTrust.RequireHttpsMetadata);
app.Logger.LogInformation(
    "Consul startup diagnostics: Address={Address}, Token={Token}, Source={Source}, KeyCount={KeyCount}, Prefixes={Prefixes}, LastError={LastError}",
    $"{consulOptions.Host}:{consulOptions.Port}",
    StartupDiagnosticsFormatter.MaskSecret(consulOptions.Token),
    consulRuntimeState.Source,
    consulRuntimeState.KeyCount,
    StartupDiagnosticsFormatter.SummarizePrefixes(consulRuntimeState.LoadedPrefixes),
    StartupDiagnosticsFormatter.SummarizeError(consulRuntimeState.LastError));
app.Logger.LogInformation("Listening: http://+:{Port}", httpPort);
if (!string.IsNullOrEmpty(connectionString))
{
    var csb = new DbConnectionStringBuilder { ConnectionString = connectionString };
    app.Logger.LogInformation("Database: PostgreSQL {Host}:{Port}/{Database}", csb["Host"], csb.TryGetValue("Port", out var dbPort) ? dbPort : "5432", csb["Database"]);
}
app.Logger.LogInformation(
    "Effective configuration diagnostics: PostgreSqlHost={PostgreSqlHost}, PostgreSqlPort={PostgreSqlPort}, PostgreSqlUsername={PostgreSqlUsername}, PostgreSqlPassword={PostgreSqlPassword}, DatabaseName={DatabaseName}",
    StartupDiagnosticsFormatter.SummarizeValue(builder.Configuration["PostgreSql:Host"]),
    StartupDiagnosticsFormatter.SummarizeValue(builder.Configuration["PostgreSql:Port"]),
    StartupDiagnosticsFormatter.SummarizeValue(builder.Configuration["PostgreSql:Username"]),
    StartupDiagnosticsFormatter.SummarizePassword(builder.Configuration["PostgreSql:Password"]),
    StartupDiagnosticsFormatter.SummarizeValue(builder.Configuration["Database:Name"]));
app.Logger.LogInformation("OSS: {OssType}", useLocalOss ? "local" : "S3");
app.Logger.LogInformation("Downstream: Student HTTP={StudentHttp}, Mistake HTTP={MistakeHttp}", studentServiceUrl, mistakeServiceUrl);
app.Logger.LogInformation("Downstream: Identity={Identity}, Teacher Portal={TeacherPortal}, Assistant Portal={AssistantPortal}",
    builder.Configuration["IdentityService:Authority"] ?? "(not configured)",
    builder.Configuration["TeacherPortal:Url"] ?? "(not configured)",
    builder.Configuration["AssistantPortal:Url"] ?? "(not configured)");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
    await DatabaseInitializer.InitializeAsync(db, loggerFactory, tableName => tableName switch
    {
        "OssAuditRuns" => @"
            CREATE TABLE IF NOT EXISTS ""OssAuditRuns"" (
                ""Id"" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                ""StartedAt"" bigint NOT NULL,
                ""CompletedAt"" bigint NULL,
                ""Status"" integer NOT NULL DEFAULT 0,
                ""NewZombieCount"" integer NOT NULL DEFAULT 0,
                ""TriggerType"" text NOT NULL DEFAULT 'scheduled',
                ""ErrorMessage"" text NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_OssAuditRuns_StartedAt"" ON ""OssAuditRuns"" (""StartedAt"");",
        "OssAuditRecords" => @"
            CREATE TABLE IF NOT EXISTS ""OssAuditRecords"" (
                ""Id"" bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                ""ObjectPath"" text NOT NULL,
                ""Bucket"" text NOT NULL,
                ""Size"" bigint NOT NULL DEFAULT 0,
                ""LastModified"" bigint NOT NULL DEFAULT 0,
                ""Status"" integer NOT NULL DEFAULT 0,
                ""CreatedAt"" bigint NOT NULL DEFAULT 0,
                ""ResolvedAt"" bigint NULL,
                ""Note"" text NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_OssAuditRecords_ObjectPath"" ON ""OssAuditRecords"" (""ObjectPath"");
            CREATE INDEX IF NOT EXISTS ""IX_OssAuditRecords_Status"" ON ""OssAuditRecords"" (""Status"");
            CREATE INDEX IF NOT EXISTS ""IX_OssAuditRecords_Bucket"" ON ""OssAuditRecords"" (""Bucket"");
            CREATE INDEX IF NOT EXISTS ""IX_OssAuditRecords_CreatedAt"" ON ""OssAuditRecords"" (""CreatedAt"");",
        _ => null
    });
}

// First middleware in the pipeline: everything registered later (CORS, static files / SPA
// fallback, authentication, the three proxies, and every /api response) runs inside its request
// scope and receives the x-correlation-id response header, injected via OnStarting before any
// response starts (static file responses included).
app.UseServiceMantleCorrelationId();

app.UseCors("AdminWeb");

// ========== Static files & SPA (before authentication) ==========
// In mode-1 integrated deployment the same container (port 5020) serves both the backend API
// and the frontend SPA (from wwwroot). The SPA entry point ("/" and all non-/api and non-/health
// routes) MUST be reachable without a JWT, otherwise the browser can never load the login page
// (deadlock: not logged in -> can't load login page -> can't log in). Authorization middleware
// and the FallbackPolicy only apply to endpoints mapped later via MapControllers().
// "/health" is excluded from the SPA rewrite so the ServiceMantle health endpoints mapped below
// stay reachable as JSON instead of being swallowed by the SPA index.html fallback.
var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    app.UseDefaultFiles();

    var appTitle = builder.Configuration["APP_TITLE"] ?? "Admin Portal";
    var escapedAppTitle = appTitle.Replace("'", "\\'");
    string? cachedIndexHtml = null;

    app.Use(async (context, next) =>
    {
        if (context.Request.Path == "/index.html")
        {
            var filePath = Path.Combine(wwwrootPath, "index.html");
            if (File.Exists(filePath))
            {
                cachedIndexHtml ??= await File.ReadAllTextAsync(filePath);
                var content = cachedIndexHtml
                    .Replace("__APP_TITLE__", appTitle)
                    .Replace("</head>", $"<script>window.__APP_TITLE__ = '{escapedAppTitle}';</script></head>");
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(content);
                return;
            }
        }
        await next();
    });

    app.UseStaticFiles();

    app.MapWhen(
        context => !context.Request.Path.StartsWithSegments("/api")
            && !context.Request.Path.StartsWithSegments("/health"),
        spaApp =>
        {
            spaApp.Use(async (context, next) =>
            {
                context.Request.Path = "/index.html";
                await next();
            });
            spaApp.UseStaticFiles();
        });
}
else
{
    // Dev fallback when wwwroot has not been built yet: anonymous health probe text.
    app.MapGet("/", () => "Student Admin WebAPI is running.").AllowAnonymous();
}

// ========== ServiceMantle health endpoints (anonymous) ==========
// The library maps GET /health/live (always 200), GET /health/ready and GET /health (readiness
// alias) WITHOUT any authorization metadata, so the FallbackPolicy (RequireAuthenticatedUser)
// would 401 them. Mapping them inside an empty route group with AllowAnonymous exempts exactly
// these endpoints while every /api endpoint keeps requiring a JWT (RuoyuJwtBearerExtensions is
// untouched). Admin registers no IServiceHealthSnapshotSource yet, so readiness is fail-closed:
// /health/ready and /health return 503 health.probe_failed; only /health/live reports success.
// Deployment and monitoring must therefore use /health/live for liveness probes (see
// docs/development/Deployment.md). Route matching is decoupled from middleware order; the
// decisive part is the /health exclusion in the SPA fallback predicate above.
var healthEndpoints = app.MapGroup(string.Empty).AllowAnonymous();
healthEndpoints.MapServiceMantleHealthEndpoints();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Admin Portal API v1"));
}
app.UseMiddleware<IdentityProxyMiddleware>();
app.UseMiddleware<TeacherPortalProxyMiddleware>();
app.UseMiddleware<AssistantPortalProxyMiddleware>();
// All /api/* endpoints are protected by the FallbackPolicy = RequireAuthenticatedUser()
// configured above. No per-controller [Authorize] attribute is required.
app.MapControllers();

app.Run();

// Exposes the implicit entry class to WebApplicationFactory<Program> for integration tests.
public partial class Program { }
