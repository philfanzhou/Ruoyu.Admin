using Admin.WebApi.Authentication;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Admin.WebApi;

internal sealed class IdentityProxyMiddleware
{
    private static readonly HashSet<string> ExcludedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transfer-Encoding", "Content-Length", "Content-Type", "Connection", "Keep-Alive"
    };

    private readonly RequestDelegate _next;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IdentityServiceOptions _options;

    public IdentityProxyMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory,
        IOptions<IdentityServiceOptions> options)
    {
        _next = next;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/identity", out var remaining))
        {
            await _next(context);
            return;
        }

        var session = context.Items[AdminSessionBoundary.TrustedSessionKey] as AdminSessionResult;
        if (session?.StatusCode != 200 || string.IsNullOrWhiteSpace(session.AccessToken))
        { await AdminSessionBoundary.RejectAsync(context, 401, "unauthorized"); return; }
        var cancellation = context.RequestAborted;
        cancellation.ThrowIfCancellationRequested();
        var client = _httpClientFactory.CreateClient("IdentityService");

        var targetPath = "/api" + remaining;
        var targetUri = $"{_options.Authority.TrimEnd('/')}{targetPath}{context.Request.QueryString}";

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
                || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || header.Key.Equals(AdminSessionBoundary.CsrfHeader, StringComparison.OrdinalIgnoreCase)) continue;
            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(header.Key, "X-Admin-AppId", StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(header.Key, "X-Admin-AppSecret", StringComparison.OrdinalIgnoreCase))
                continue;
            requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session!.AccessToken);

        if (!string.IsNullOrEmpty(_options.AppId))
            requestMessage.Headers.TryAddWithoutValidation("X-Admin-AppId", _options.AppId);
        if (!string.IsNullOrEmpty(_options.AppSecret))
            requestMessage.Headers.TryAddWithoutValidation("X-Admin-AppSecret", _options.AppSecret);

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
            await context.Response.WriteAsync("{\"message\":\"Identity service unreachable\"}", cancellation).ConfigureAwait(false);
            return;
        }

        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;
            if (response.Content.Headers.ContentType is { } contentType)
            {
                context.Response.ContentType = contentType.ToString();
            }

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

public sealed class IdentityServiceOptions
{
    public const string SectionName = "IdentityService";

    public string Authority { get; set; } = string.Empty;
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
}
