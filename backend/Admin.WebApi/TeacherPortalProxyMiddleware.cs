using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Admin.WebApi;

internal sealed class TeacherPortalProxyMiddleware
{
    private static readonly HashSet<string> ExcludedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transfer-Encoding", "Content-Length", "Content-Type", "Connection", "Keep-Alive"
    };

    private readonly RequestDelegate _next;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TeacherPortalOptions _options;

    public TeacherPortalProxyMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory,
        IOptions<TeacherPortalOptions> options)
    {
        _next = next;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/teacher-portal"))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.InternalUrl) || string.IsNullOrWhiteSpace(_options.AdminApiKey))
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"message\":\"Teacher portal not configured\"}").ConfigureAwait(false);
            return;
        }

        var client = _httpClientFactory.CreateClient("TeacherPortal");

        var pathValue = context.Request.Path.Value!;
        string targetPath;
        if (pathValue.StartsWith("/api/teacher-portal/admin", StringComparison.OrdinalIgnoreCase))
        {
            targetPath = pathValue.Replace("/api/teacher-portal/admin", "/api/admin");
        }
        else if (pathValue.StartsWith("/api/teacher-portal/auth", StringComparison.OrdinalIgnoreCase))
        {
            targetPath = pathValue.Replace("/api/teacher-portal/auth", "/api/auth");
        }
        else
        {
            targetPath = pathValue.Replace("/api/teacher-portal", "/api/admin");
        }

        var targetUri = $"{_options.InternalUrl.TrimEnd('/')}{targetPath}{context.Request.QueryString}";

        var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        if (context.Request.Body != null && !HttpMethods.IsGet(context.Request.Method))
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(body))
            {
                var content = new StringContent(body, Encoding.UTF8);
                content.Headers.ContentType = new MediaTypeHeaderValue(context.Request.ContentType ?? "application/json");
                requestMessage.Content = content;
            }
        }

        foreach (var header in context.Request.Headers)
        {
            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                continue;
            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        requestMessage.Headers.Add("X-Admin-Key", _options.AdminApiKey);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        }
        catch
        {
            context.Response.StatusCode = 502;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"message\":\"Teacher portal service unreachable\"}").ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = (int)response.StatusCode;

        foreach (var header in response.Headers)
        {
            if (ExcludedResponseHeaders.Contains(header.Key)) continue;
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }
        foreach (var header in response.Content.Headers)
        {
            if (ExcludedResponseHeaders.Contains(header.Key)) continue;
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        await response.Content.CopyToAsync(context.Response.Body).ConfigureAwait(false);
    }
}

public sealed class TeacherPortalOptions
{
    public const string SectionName = "TeacherPortal";

    public string InternalUrl { get; set; } = "";
    public string AdminApiKey { get; set; } = "";
}
