using Admin.WebApi;
using Admin.WebApi.Data;
using Admin.WebApi.Services;
using Grpc.Net.Client;
using Grpc.Net.ClientFactory;
using Microsoft.EntityFrameworkCore;
using Ruoyu.Study.Common.Database;
using Ruoyu.Study.Common.Oss;
using Ruoyu.Study.Student.Contract.Protos;
using MistakeProto = Ruoyu.Study.Mistake.Contract.Protos;

var builder = WebApplication.CreateBuilder(args);

var adminApiPort = builder.Configuration.GetValue<int>("AdminApi:Port");
var grpcServiceAddress = builder.Configuration["StudentGrpcService:Address"] ?? "http://localhost:5005";
var mistakeGrpcAddress = builder.Configuration["MistakeGrpcService:Address"] ?? "http://localhost:5006";

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(adminApiPort);
});

// Configure gRPC clients with proper HTTP/2 handler
builder.Services.AddGrpcClient<StudentManagementGrpcService.StudentManagementGrpcServiceClient>(options =>
{
    options.Address = new Uri(grpcServiceAddress);
}).ConfigureChannel(options =>
{
    options.HttpHandler = new SocketsHttpHandler
    {
        EnableMultipleHttp2Connections = true,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        // Disable TLS validation for internal Docker network
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
        }
    };
});

builder.Services.AddGrpcClient<StudentLearningGrpcService.StudentLearningGrpcServiceClient>(options =>
{
    options.Address = new Uri(grpcServiceAddress);
}).ConfigureChannel(options =>
{
    options.HttpHandler = new SocketsHttpHandler
    {
        EnableMultipleHttp2Connections = true,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        // Disable TLS validation for internal Docker network
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
        }
    };
});

builder.Services.AddGrpcClient<MistakeProto.MistakeGrpcService.MistakeGrpcServiceClient>(options =>
{
    options.Address = new Uri(mistakeGrpcAddress);
}).ConfigureChannel(options =>
{
    options.HttpHandler = new SocketsHttpHandler
    {
        EnableMultipleHttp2Connections = true,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        // Disable TLS validation for internal Docker network
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
        }
    };
});

builder.Services.Configure<IdentityServiceOptions>(
    builder.Configuration.GetSection(IdentityServiceOptions.SectionName));

builder.Services.Configure<TeacherPortalOptions>(
    builder.Configuration.GetSection(TeacherPortalOptions.SectionName));

builder.Services.AddHttpClient("IdentityService", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("TeacherPortal", client =>
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

builder.Services.AddSingleton<IOssService>(sp =>
{
    var ossOptions = builder.Configuration.GetSection("Oss").Get<OssOptions>() ?? new OssOptions();
    return new S3OssService(
        ossOptions.Endpoint,
        ossOptions.AccessKey,
        ossOptions.SecretKey,
        ossOptions.BucketName);
});

var connectionString = builder.Configuration.GetConnectionString("AuditDb")
    ?? "Host=localhost;Port=5432;Database=ruoyu_admin;Username=postgres;Password=postgres";

builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSingleton<OssAuditWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OssAuditWorker>());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DatabaseInitializer.InitializeAsync(db, logger);
}

app.UseCors("AdminWeb");
app.UseMiddleware<IdentityProxyMiddleware>();
app.UseMiddleware<TeacherPortalProxyMiddleware>();
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
