using Admin.WebApi;
using Admin.WebApi.Models;
using Grpc.Net.ClientFactory;
using Ruoyu.Study.Student.Contract.Protos;

var builder = WebApplication.CreateBuilder(args);

var adminApiPort = builder.Configuration.GetValue<int>("AdminApi:Port");
var adminAppId = builder.Configuration["AdminApi:AppId"] ?? string.Empty;
var adminAppSecret = builder.Configuration["AdminApi:AppSecret"] ?? string.Empty;
var grpcServiceAddress = builder.Configuration["GrpcService:Address"] ?? "http://localhost:5005";

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(adminApiPort);
});

builder.Services.AddSingleton(new AdminCredentials(adminAppId, adminAppSecret));
builder.Services.AddGrpcClient<StudentManagementGrpcService.StudentManagementGrpcServiceClient>(options =>
{
    options.Address = new Uri(grpcServiceAddress);
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
app.MapStudentAdminApi(adminApiPort);

// ========== Static files & SPA ==========
var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    app.UseDefaultFiles();

    // Inject app title from env var (APP_TITLE) into index.html at runtime
    var appTitle = builder.Configuration["APP_TITLE"] ?? "Admin Portal";
    app.Use(async (context, next) =>
    {
        if (context.Request.Path == "/index.html")
        {
            var filePath = Path.Combine(wwwrootPath, "index.html");
            if (File.Exists(filePath))
            {
                var content = await File.ReadAllTextAsync(filePath);
                content = content.Replace("__APP_TITLE__", appTitle);
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(content);
                return;
            }
        }
        await next();
    });

    app.UseStaticFiles();

    // SPA fallback for non-API paths (Vue Router history mode)
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
