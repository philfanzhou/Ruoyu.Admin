using Admin.WebApi;
using Admin.WebApi.Models;
using Grpc.Net.ClientFactory;
using Ruoyu.Study.Student.Contract.Protos;

var builder = WebApplication.CreateBuilder(args);

var adminApiPort = builder.Configuration.GetValue<int>("AdminApi:Port");
var grpcServiceAddress = builder.Configuration["StudentGrpcService:Address"] ?? "http://localhost:5005";

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(adminApiPort);
});

builder.Services.AddGrpcClient<StudentManagementGrpcService.StudentManagementGrpcServiceClient>(options =>
{
    options.Address = new Uri(grpcServiceAddress);
});

builder.Services.Configure<IdentityServiceOptions>(
    builder.Configuration.GetSection(IdentityServiceOptions.SectionName));

builder.Services.AddHttpClient("IdentityService", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
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

var app = builder.Build();

app.UseCors("AdminWeb");
app.UseMiddleware<IdentityProxyMiddleware>();
app.MapStudentAdminApi(adminApiPort);

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
