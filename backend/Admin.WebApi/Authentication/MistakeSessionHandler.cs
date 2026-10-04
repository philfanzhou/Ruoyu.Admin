using System.Net.Http.Headers;
using OpenTelemetry;
using Ruoyu.Admin.ServiceClients;

namespace Admin.WebApi.Authentication;

// A pooled handler holds no request scope, principal or credential. Resolve the CURRENT scope on EVERY send.
internal sealed class MistakeSessionHandler(IHttpContextAccessor contexts, MistakeSessionSettings settings) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = contexts.HttpContext ?? throw new MistakeDownstreamException();
        request.Options.Set(MistakeClientPolicy.RequestAborted, context.RequestAborted);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.RequestAborted);
        var ct = linked.Token;
        ct.ThrowIfCancellationRequested();
        if (!IsAllowed(request, settings.Origin!)) throw new MistakeDownstreamException();
        var session = await context.RequestServices.GetRequiredService<AdminSessionAccessor>().AuthenticateAsync(context, currentTicket: true);
        ct.ThrowIfCancellationRequested();
        if (session.StatusCode != 200) throw new MistakeSessionException(session.StatusCode, session.Error!);
        request.Headers.Clear();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        // Checking again closes the cancellation interval introduced by asynchronous authentication.
        ct.ThrowIfCancellationRequested();
        using var suppression = SuppressInstrumentationScope.Begin();
        var response = await base.SendAsync(request, ct);
        try
        {
            // Keep disconnect cancellation active while buffering the downstream body as well.
            await response.Content.LoadIntoBufferAsync(ct);
            ct.ThrowIfCancellationRequested();
            return response;
        }
        catch { response.Dispose(); throw; }
    }

    internal static bool IsAllowed(HttpRequestMessage request, Uri origin)
    {
        var uri = request.RequestUri;
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != origin.Scheme || uri.Host != origin.Host || uri.Port != origin.Port
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        var parts = uri.AbsolutePath.Split('/');
        if (parts.Length < 3 || parts[0] != "" || parts[1] != "api" || parts[2] != "mistakes") return false;
        var suffix = parts.Skip(3).ToArray();
        if (suffix.Any(segment => !SafeSegment(segment))) return false;
        var method = request.Method;
        if (suffix.Length == 0) return method == HttpMethod.Get || method == HttpMethod.Post;
        if (suffix.Length == 1)
        {
            return suffix[0] switch
            {
                "pending-reviews" => method == HttpMethod.Get,
                "upload" or "complete-review" or "presigned-url" => method == HttpMethod.Post,
                "by-upload" or "reanalyze" => false,
                _ => method == HttpMethod.Get || method == HttpMethod.Put || method == HttpMethod.Delete
            };
        }
        if (suffix.Length != 2) return false;
        if (suffix[0] == "by-upload") return method == HttpMethod.Get;
        if (suffix[0] == "reanalyze") return suffix[1] == "jobs" && method == HttpMethod.Post
            || suffix[1] == "active" && method == HttpMethod.Get;
        return suffix[1] is "review" or "reanalyze" && method == HttpMethod.Post;
    }

    private static bool SafeSegment(string segment)
    {
        if (segment.Length == 0) return false;
        // Servers/proxies may decode more than once. No encoded delimiter can change a route.
        var decoded = segment;
        for (var i = 0; i < 4; i++)
        {
            if (decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\\') || decoded.Any(char.IsControl)) return false;
            var next = Uri.UnescapeDataString(decoded);
            if (next == decoded) return true;
            decoded = next;
        }
        return false;
    }
}
