using System.Net.Http.Headers;
using System.Text;
using Admin.WebApi;
using Admin.WebApi.Models;
using Grpc.Net.ClientFactory;
using Ruoyu.Study.Student.Contract.Protos;

var builder = WebApplication.CreateBuilder(args);

var adminApiPort = builder.Configuration.GetValue<int>("AdminApi:Port");
var grpcServiceAddress = builder.Configuration["GrpcService:Address"] ?? "http://localhost:5005";

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(adminApiPort);
});

builder.Services.AddGrpcClient<StudentManagementGrpcService.StudentManagementGrpcServiceClient>(options =>
{
    options.Address = new Uri(grpcServiceAddress);
});

var identityServiceAddress = builder.Configuration["IdentityService:Address"] ?? "http://localhost:5002";
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
app.MapStudentAdminApi(adminApiPort);

// Proxy /api/identity/* requests to IdentityIssuer service
app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/api/identity"), identityApp =>
{
    identityApp.Run(async context =>
    {
        var clientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
        var client = clientFactory.CreateClient("IdentityService");

        var targetPath = context.Request.Path.Value!.Replace("/api/identity", "/api");
        var targetUri = $"{identityServiceAddress.TrimEnd('/')}{targetPath}{context.Request.QueryString}";

        var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        // Forward request body for methods that support it
        if (context.Request.Body != null && !HttpMethods.IsGet(context.Request.Method))
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            if (!string.IsNullOrEmpty(body))
            {
                var content = new StringContent(body, Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue(context.Request.ContentType ?? "application/json");
                requestMessage.Content = content;
            }
        }

        // Forward request headers (Content-* handled by StringContent above)
        foreach (var header in context.Request.Headers)
        {
            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                continue;
            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
        }
        catch
        {
            context.Response.StatusCode = 502;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync($"{{\"message\":\"Identity service unreachable\"}}");
            return;
        }

        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
            context.Response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in response.Content.Headers)
            context.Response.Headers[header.Key] = header.Value.ToArray();

        await response.Content.CopyToAsync(context.Response.Body);
    });
});

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
                // Inject global variable for Vue app to read at runtime
                content = content.Replace("</head>", $"<script>window.__APP_TITLE__ = '{appTitle.Replace("'", "\\'")}';</script></head>");
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
