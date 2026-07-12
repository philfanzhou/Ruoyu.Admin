using System.Data.Common;
using Admin.WebApi;
using Admin.WebApi.Persistence;
using Admin.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Ruoyu.Study.Common.Database;
using Ruoyu.Study.Common.Oss;
using Ruoyu.Study.MistakeBff.GrpcClients;

var builder = WebApplication.CreateBuilder(args);

var adminApiPort = builder.Configuration.GetValue<int>("AdminApi:Port");
var studentServiceUrl = builder.Configuration["StudentService:Url"] ?? "http://localhost:5005";
var mistakeServiceUrl = builder.Configuration["MistakeService:BaseUrl"] ?? "http://localhost:5007";

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(adminApiPort);
});

// Configure downstream clients
// Student service: HTTP (migrated from gRPC)
builder.Services.AddStudentHttpClient(studentServiceUrl);

// Mistake service: HTTP (migrated from gRPC)
builder.Services.AddMistakeHttpClient(mistakeServiceUrl);

builder.Services.Configure<IdentityServiceOptions>(
    builder.Configuration.GetSection(IdentityServiceOptions.SectionName));

builder.Services.Configure<TeacherPortalOptions>(
    builder.Configuration.GetSection(TeacherPortalOptions.SectionName));

builder.Services.Configure<AssistantPortalOptions>(
    builder.Configuration.GetSection(AssistantPortalOptions.SectionName));

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
    return new S3OssService(
        ossOptions.Endpoint,
        ossOptions.AccessKey,
        ossOptions.SecretKey,
        ossOptions.BucketName,
        publicEndpoint: ossOptions.PublicEndpoint);
    // 不配置 allowedPrefixes：Admin Portal 需要访问所有路径前缀（审计+运维）
});

var connectionString = builder.Configuration.GetConnectionString("AuditDb")
    ?? "Host=localhost;Port=5432;Database=ruoyu_admin;Username=postgres;Password=postgres";
var isPostgreSql = connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase)
                || connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<AuditDbContext>(options =>
{
    if (isPostgreSql)
        options.UseNpgsql(connectionString);
    else
        options.UseSqlite(connectionString);
});

if (!isPostgreSql)
{
    var dir = Path.GetDirectoryName(connectionString.Replace("Data Source=", ""));
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);
}

var useLocalDb = !isPostgreSql;
builder.Services.AddSingleton<OssAuditWorker>();
if (!useLocalDb)
{
    builder.Services.AddHostedService(sp => sp.GetRequiredService<OssAuditWorker>());
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Admin Portal API", Version = "v1" });
});

var app = builder.Build();

app.Logger.LogInformation("Admin Portal starting");
app.Logger.LogInformation("Listening: port {Port}", adminApiPort);
if (isPostgreSql)
{
    var csb = new DbConnectionStringBuilder { ConnectionString = connectionString };
    app.Logger.LogInformation("Database: PostgreSQL {Host}:{Port}/{Database}", csb["Host"], csb.TryGetValue("Port", out var dbPort) ? dbPort : "5432", csb["Database"]);
}
else
{
    app.Logger.LogInformation("Database: SQLite");
}
app.Logger.LogInformation("OSS: {OssType}", useLocalOss ? "local" : "S3");
app.Logger.LogInformation("Downstream: Student HTTP={StudentHttp}, Mistake HTTP={MistakeHttp}", studentServiceUrl, mistakeServiceUrl);
app.Logger.LogInformation("Downstream: Identity={Identity}, Teacher Portal={TeacherPortal}, Assistant Portal={AssistantPortal}",
    builder.Configuration["IdentityService:Address"] ?? "(not configured)",
    builder.Configuration["TeacherPortal:Address"] ?? "(not configured)",
    builder.Configuration["AssistantPortal:Address"] ?? "(not configured)");

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

app.UseCors("AdminWeb");
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Admin Portal API v1"));
}
app.UseMiddleware<IdentityProxyMiddleware>();
app.UseMiddleware<TeacherPortalProxyMiddleware>();
app.UseMiddleware<AssistantPortalProxyMiddleware>();
app.MapControllers();

// ========== Static files & SPA ==========
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
        context => !context.Request.Path.StartsWithSegments("/api"),
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
    app.MapGet("/", () => "Student Admin WebAPI is running.");
}

app.Run();
