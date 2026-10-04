using Admin.WebApi.Authentication;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Admin.WebApi;

internal sealed class AssistantPortalProxyMiddleware
{
    private static readonly HashSet<string> ExcludedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transfer-Encoding", "Content-Length", "Content-Type", "Connection", "Keep-Alive"
    };

    private readonly RequestDelegate _next;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AssistantPortalOptions _options;

    public AssistantPortalProxyMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory,
        IOptions<AssistantPortalOptions> options)
    {
        _next = next;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/assistant-portal", out var remaining))
        {
            await _next(context);
            return;
        }

        var session = context.Items[AdminSessionBoundary.TrustedSessionKey] as AdminSessionResult;
        if (session?.StatusCode != 200 || string.IsNullOrWhiteSpace(session.AccessToken))
        { await AdminSessionBoundary.RejectAsync(context, 401, "unauthorized"); return; }
        var cancellation = context.RequestAborted;
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"message\":\"Assistant portal not configured\"}", cancellation).ConfigureAwait(false);
            return;
        }

        var client = _httpClientFactory.CreateClient("AssistantPortal");

        string targetPath;
        if (remaining.StartsWithSegments("/admin", out var adminSuffix)) targetPath = "/api/admin" + adminSuffix;
        else if (remaining.StartsWithSegments("/auth", out var authSuffix)) targetPath = "/api/auth" + authSuffix;
        else targetPath = "/api/admin" + remaining;

        var targetUri = $"{_options.Url.TrimEnd('/')}{targetPath}{context.Request.QueryString}";

        using var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        if (context.Request.Body != null && !HttpMethods.IsGet(context.Request.Method))
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync(cancellation).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(body))
            {
                var content = new StringContent(body, Encoding.UTF8);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType ?? "application/json");
                requestMessage.Content = content;
            }
        }

        foreach (var header in context.Request.Headers)
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals(AdminSessionBoundary.CsrfHeader, StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("X-Admin-AppId", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals("X-Admin-AppSecret", StringComparison.OrdinalIgnoreCase)) continue;
            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                continue;
            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
        catch
        {
            context.Response.StatusCode = 502;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync("{\"message\":\"Assistant portal service unreachable\"}", cancellation).ConfigureAwait(false);
            return;
        }

        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;

            foreach (var header in response.Headers)
            {
                if (ExcludedResponseHeaders.Contains(header.Key) || header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)) continue;
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            foreach (var header in response.Content.Headers)
            {
                if (ExcludedResponseHeaders.Contains(header.Key) || header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)) continue;
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            await response.Content.CopyToAsync(context.Response.Body, cancellation).ConfigureAwait(false);
        }
    }
}

public sealed class AssistantPortalOptions
{
    public const string SectionName = "AssistantPortal";

    public string Url { get; set; } = "";
}
