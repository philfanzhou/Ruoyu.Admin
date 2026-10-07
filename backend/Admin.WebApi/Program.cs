using System.Data.Common;
using Admin.WebApi;
using Admin.WebApi.Authentication;
using Admin.WebApi.Controllers;
using Admin.WebApi.Database;
using Admin.WebApi.Models;
using Admin.WebApi.Persistence;
using Admin.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Ruoyu.Admin.Common.Oss;
using Ruoyu.Admin.Consul;
using Ruoyu.Admin.ServiceClients;
using ServiceMantle;
using SignaCore.Client.AspNetCore;
using ServiceMantle.Bootstrap;
using ServiceMantle.Database.PostgreSql;
using ServiceMantle.Database.PostgreSql.Migration;
using ServiceMantle.Persistence.Relational;
using ServiceMantle.Migration;

var validateAuthConfig = args.Contains("--validate-auth-config", StringComparer.Ordinal);
WebApplicationBuilder builder;
try
{
    builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--validate-auth-config").ToArray());
    builder.Configuration.AddRuoyuConsulConfiguration(builder.Configuration, readOnly: validateAuthConfig);
    _ = AdminOidcSettings.Read(builder.Configuration, builder.Environment);
    _ = MistakeSessionSettings.Read(builder.Configuration, builder.Environment);
}
catch (Exception error)
{
    // Configuration values and provider/driver exceptions never enter startup output.
    Console.Error.WriteLine("RUOYU_ADMIN_AUTH_CONFIG_INVALID");
    if (!validateAuthConfig)
    {
        var safeKey = error is InvalidOperationException && error.Message is
            "AdminOidc:Enabled" or "AdminOidc:UseSessionForAdminApi" or "AdminOidc:UseSessionForLogout"
            or "AdminOidc:UseSessionForIdentityProxy" or "AdminOidc:UseSessionForPortalProxies"
            or "AdminOidc:RedirectUri" or "AdminOidc:PostLogoutRedirectUri"
            or "IdentityService:Authority" or "IdentityService:AppId" or "IdentityService:AppSecret"
            or "IdentityService:ClockSkewSeconds" or "MistakeService:UseSessionToken" or "MistakeService:Url" ? error.Message : "RUOYU_ADMIN_AUTH_CONFIG_INVALID";
        throw new InvalidOperationException(safeKey);
    }
    Environment.ExitCode = 2;
    return;
}
if (validateAuthConfig)
{
    Console.WriteLine("RUOYU_ADMIN_AUTH_CONFIG_VALID");
    return;
}

var consulOptions = RuoyuConsulOptions.Bind(builder.Configuration);
var consulRuntimeState = RuoyuConsulRuntimeState.Instance;

// ========== ServiceMantle logging (Console + optional Grafana Loki) ==========
builder.AddAdminLogging();

// HTTP listen port is hardcoded to 5020 (not configurable).
// nginx in the same container proxies /api/ to this port.
const int httpPort = 5020;

var studentServiceUrl = builder.Configuration["StudentService:Url"] ?? "http://localhost:5005";
var mistakeServiceUrl = builder.Configuration["MistakeService:Url"] ?? "http://localhost:5007";
var managedAssignment = ManagedAssignmentOptions.Read(builder.Configuration);
builder.Services.AddSingleton(managedAssignment);
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
builder.Services.AddHttpClient<IStudentHttpClient, StudentHttpClient>()
    .AddServiceMantleCorrelationIdPropagation();

// Mistake service: HTTP (migrated from gRPC)
var mistakeSessionSettings = MistakeSessionSettings.Read(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(mistakeSessionSettings);
builder.Services.AddSingleton(new MistakeClientPolicy(mistakeSessionSettings.UseSessionToken));
builder.Services.AddMistakeHttpClient(mistakeServiceUrl);
if (mistakeSessionSettings.UseSessionToken)
{
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddTransient<MistakeSessionHandler>();
    builder.Services.AddHttpClient<IMistakeHttpClient, MistakeHttpClient>()
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ActivityHeadersPropagator = null })
        .AddHttpMessageHandler<MistakeSessionHandler>()
        .RemoveAllLoggers();
}
// Run after the session handler has established the only downstream credential.
builder.Services.AddHttpClient<IMistakeHttpClient, MistakeHttpClient>()
    .AddServiceMantleCorrelationIdPropagation();
if (managedAssignment.Enabled)
{
    // Same handler hygiene as the session mistake client: no redirects, no cookie jar,
    // no activity headers, no logging handlers (the request carries a caller token).
    builder.Services.AddHttpClient<IManagedMistakeHttpClient, ManagedMistakeHttpClient>(client =>
    {
        client.BaseAddress = new Uri(mistakeServiceUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
    }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ActivityHeadersPropagator = null })
      .AddServiceMantleCorrelationIdPropagation()
      .RemoveAllLoggers();
}
builder.Services.AddHttpClient<HomeworkReferenceClient>(client =>
{
    client.BaseAddress = new Uri(homeworkServiceUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddServiceMantleCorrelationIdPropagation();

// The only inbound scheme is the server-side session; downstream tokens remain server-side.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddAdminOidc(builder.Configuration, builder.Environment);

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
}).AddServiceMantleCorrelationIdPropagation();

builder.Services.AddHttpClient("TeacherPortal", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddServiceMantleCorrelationIdPropagation();

builder.Services.AddHttpClient("AssistantPortal", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddServiceMantleCorrelationIdPropagation();

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

builder.Services.AddControllers(options =>
{
    // Marks every action of controllers carrying [RequireSecurityResponseHeaders] with the
    // ServiceMantle security response-header metadata (see the attribute's doc comment).
    options.Conventions.Add(new AdminSecurityResponseHeadersConvention());
    options.Filters.Add<MistakeSessionFailureFilter>();
});

// Resolve and validate consumer configuration before registering the shared gate. Parsing
// failures carry only a safe code, without the original value or driver exception.
var startupDatabaseOptions = AuditDatabaseStartupConfiguration.Read(builder.Configuration);
var connectionString = startupDatabaseOptions.Database.ConnectionString;
builder.Services.AddServiceMantlePostgreSqlDeploymentCapability();

// ========== ServiceMantle (service identity, correlation id, base telemetry, health) ==========
// ServiceId "ruoyu-admin" is the stable deployment identity (lowercase; deliberately distinct
// from the fixed Loki stream label "Ruoyu.Admin"). The InstanceId is regenerated on every host
// build and is NOT a persistent identity: it changes on each restart. No bootstrapFilePath is
// passed: the bootstrap store stays a lazy singleton and this wiring performs zero disk writes.
// No serviceVersion is passed: it resolves from the entry assembly informational version.
// AddOpenTelemetryInstrumentation uses the default options (AspNetCore / HttpClient / Runtime
// instrumentation) and registers NO exporter.
// AddServiceMantleHealthEndpoints registers the health endpoint services ONLY (the actual
// /health/live, /health/ready and /health routes are mapped below) with a 3-second probe
// budget: a wedged database cannot hold the readiness request forever (an exhausted budget
// maps to the library's fixed health.probe_timeout). Readiness evidence comes from the
// shared EF Core snapshot source below; no readiness contributor is registered
// (downstream services deliberately stay OUT of readiness to avoid cascading removal).
// AddExceptionMapping pins the single exact type downstream failures surface as: every
// HttpRequestException escaping a JSON controller action answers 502 problem+json with the
// fixed code downstream.unavailable and never the downstream message. Per-status relays
// (404/400 passthrough from downstream bodies) were deliberately removed with the ex.Message
// catches; per-status preservation would need conditional mappings (ServiceMantle.Web 0.2.1+).
// AddSecurityResponseHeaders registers the six-header security response baseline services
// (immutable Cache-Control/Pragma/X-Content-Type-Options/X-Frame-Options/Referrer-Policy/CSP);
// the actual header writes happen in UseServiceMantleSecurityResponseHeaders below and only for
// endpoints marked via [RequireSecurityResponseHeaders] / RequireServiceMantleSecurityResponseHeaders.
builder.Services
    .AddServiceMantle(
        ServiceId.Parse("ruoyu-admin"),
        InstanceId.CreateRandom(ServiceId.Parse("ruoyu-admin")))
    .AddOpenTelemetryInstrumentation()
    .AddServiceMantleHealthEndpoints(options =>
    {
        options.ProbeTimeout = TimeSpan.FromSeconds(3);
    })
    .AddExceptionMapping<HttpRequestException>(
        StatusCodes.Status502BadGateway,
        "downstream.unavailable",
        "A downstream service request failed.")
    .AddExceptionMapping<MistakeDownstreamException>(StatusCodes.Status502BadGateway,
        "mistake.unavailable", "The Mistake service request failed.")
    .AddExceptionMapping<MistakeBadRequestException>(StatusCodes.Status400BadRequest,
        "mistake.request_rejected", "The Mistake service rejected the request.")
    .AddExceptionMapping<MistakeConflictException>(StatusCodes.Status409Conflict,
        "mistake.conflict", "The Mistake service rejected the conflicting request.")
    .AddSecurityResponseHeaders()
    // ========== Shared startup database gate (issue #66) ==========
    // The PostgreSQL session advisory lock that serializes multi-instance startup: it covers the
    // orchestrator's initial inspection, the legacy takeover, the EF Core migration execution,
    // and the final inspection (never only the Migrate call). The consuming service's executor
    // (AuditMigrationExecutor) plus the scoped orchestrator: each scope resolves its own
    // executor instance; the lock/state machine belongs to the shared orchestrator and is never
    // reimplemented locally.
    .AddMigrationLockProvider<PostgreSqlMigrationLockProvider>()
    .AddDatabaseTargetPreparationProvider<PostgreSqlDatabaseTargetPreparationProvider>()
    .AddDatabaseMigration<AuditMigrationExecutor>()
    .AddStartupDatabaseGate(startupDatabaseOptions);
// The orchestrator activates the executor through DI; this registration keeps the executor's
// diagnostic logging (baseline applied / takeover / refusal reasons) on a named category logger
// instead of silently degrading to the null logger. Without it the optional ILogger constructor
// parameter defaults to null.
builder.Services.AddSingleton<Microsoft.Extensions.Logging.ILogger>(
    serviceProvider => serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
        .CreateLogger(nameof(AuditMigrationExecutor)));

// The gate's singleton receipt and a fresh scoped EF context provide readiness evidence.
// Downstream services stay outside readiness; the schema probe only reads mapped tables/columns.
builder.Services.AddServiceMantleEfCoreHealthSnapshotSource<AuditDbContext>(
    ServiceId.Parse("ruoyu-admin"),
    new PostgreSqlDatabaseProbeFailureClassifier(),
    probeMode: EfCoreHealthSnapshotProbeMode.MappedSchema,
    errorCodePrefix: "ruoyu-admin");

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

builder.Services.AddDbContext<AuditDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddSingleton(new StorageReferenceSigningConfiguration(builder.Configuration));
builder.Services.AddHttpClient(StorageReferenceCollector.ClientName, client => client.Timeout = TimeSpan.FromSeconds(35))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
    .AddServiceMantleCorrelationIdPropagation();
builder.Services.AddScoped<IStorageReferenceCollector, StorageReferenceCollector>();
builder.Services.AddSingleton<OssAuditWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OssAuditWorker>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Admin Portal API", Version = "v1" });
});

var app = builder.Build();
app.Logger.LogInformation("Admin Portal starting");
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

// AddStartupDatabaseGate runs once in IHostedLifecycleService.StartingAsync, before the
// web host and OssAuditWorker StartAsync. No direct gate call or second receipt is needed.

// First middleware in the pipeline: everything registered later (CORS, static files / SPA
// fallback, authentication, the three proxies, and every /api response) runs inside its request
// scope and receives the x-correlation-id response header, injected via OnStarting before any
// response starts (static file responses included).
app.UseServiceMantleCorrelationId();

// ========== Safe Problem Details boundary (JSON controller endpoints only) ==========
// Unhandled exceptions escaping a JSON controller action are converted to deterministic
// application/problem+json (type/title/status/correlationId/errorCode; mapped
// HttpRequestException -> 502 downstream.unavailable, everything else -> the library's fixed
// 500 http.internal_server_error) while the response has not started; exception messages,
// stacks and Data never reach the response. The branch predicate selects exactly the MVC
// controller actions except ImageController: the SPA, static files, health endpoints and the
// marker-only auth routes are minimal-API endpoints without ControllerActionDescriptor, the
// three proxy paths have no routed endpoint at all, and the image surface keeps its own fixed
// error responses. Route selection already happened (implicit UseRouting runs first), so the
// predicate sees the endpoint. A UseWhen branch builds a separate IApplicationBuilder, which
// keeps the library's PipelineComposition state of the main pipeline untouched. Caller
// cancellation propagates; a response that already started is left exactly as sent.
app.UseWhen(
    context => context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>()
            is { ControllerTypeInfo: { } controllerType } && controllerType != typeof(ImageController),
    branch => branch.UseServiceMantleProblemDetails());

app.UseCors("AdminWeb");

// ========== ServiceMantle security response headers (after route selection) ==========
// Route matching runs before any user middleware in WebApplication hosting (no explicit
// UseRouting below), so the selected endpoint's metadata is already available here. The
// middleware is a no-op for unmarked endpoints, which keeps static files, the SPA fallback,
// the health endpoints, ImageController redirects and the three proxy middlewares' forwarded
// responses on their existing header behavior. Marked endpoints (controllers carrying
// [RequireSecurityResponseHeaders] plus the marker-only auth endpoints mapped further down)
// receive the immutable six-header baseline on every routed response while headers are unsent
// — including 401/403 challenges and middleware short-circuits such as the session boundary
// rejections, because those happen after route selection.
app.UseServiceMantleSecurityResponseHeaders();

// ========== Static files & SPA (before authentication) ==========
// In mode-1 integrated deployment the same container (port 5020) serves both the backend API
// and the frontend SPA (from wwwroot). The SPA entry point ("/" and all non-/api and non-/health
// routes) MUST be reachable without a session, otherwise the browser can never load the login page
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
// these endpoints while every /api endpoint keeps requiring a session. Readiness projects
// the shared EF Core snapshot source: ready (200) only
// for Completed + Succeeded + Reachable, i.e. this process finished its migration orchestration AND a
// bounded read-only AuditDb probe of the mapped tables succeeded; every failure answers 503
// with a fixed safe errorCode and never a connection string, host, or exception text.
// Downstream services (Student/Mistake/Identity/...) are deliberately NOT part of readiness to
// avoid cascading removal. Route matching is decoupled from middleware order; the decisive part
// is the /health exclusion in the SPA fallback predicate above.
var healthEndpoints = app.MapGroup(string.Empty).AllowAnonymous();
healthEndpoints.MapServiceMantleHealthEndpoints();

// ========== SignaCore hosted-login endpoints (package-owned, anonymous) ==========
// MapSignaCoreHostedLogin mounts /api/auth/oidc/start, /callback (exactly the registered
// RedirectUri path), /session, /signin-failed, /csrf, POST /logout, and /logout/return. They
// are mapped inside an empty AllowAnonymous group exactly like the health endpoints: the
// FallbackPolicy below would otherwise 401 them, and the package owns each endpoint's own
// gate (antiforgery, session cookie, one-time state) instead of the ambient authorization.
var hostedLogin = app.MapGroup(string.Empty).AllowAnonymous();
hostedLogin.MapSignaCoreHostedLogin(AdminOidcSettings.HostedLoginPrefix);

// Explicit session authentication runs inside this boundary; retired password login
// returns 410 before body binding or any Identity request. The logout branch keeps the
// previous middleware contract deterministically: only POST is ever answered (any other
// method is a plain 405 before the authorization pipeline can challenge), and every POST
// also retires the legacy adminAuthToken cookie of the password era, matching the old
// local-session-first logout contract.
app.Use(async (context, next) =>
{
    if (AdminSessionBoundary.IsAuthPath(context.Request.Path, AdminOidcSettings.LogoutPath))
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }
        context.Response.Cookies.Delete("adminAuthToken", new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
    }
    await next(context);
});
app.UseMiddleware<AdminSessionMiddleware>();
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
