using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Admin.WebApi.Authentication;

// Framework protocol diagnostics include Location, cookies and untrusted response/exception
// details at Debug/Error. Keep those out of every configured logging provider, even at Trace.
internal sealed class SafeOpenIdConnectHandler(IOptionsMonitor<OpenIdConnectOptions> options,
    HtmlEncoder htmlEncoder, UrlEncoder encoder) : OpenIdConnectHandler(options, NullLoggerFactory.Instance, htmlEncoder, encoder)
{
    public override Task<bool> HandleRequestAsync() => Request.Path == AdminOidcSettings.CallbackPath
        ? base.HandleRequestAsync() : Task.FromResult(false);

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        try { Context.RequestAborted.ThrowIfCancellationRequested(); await base.HandleChallengeAsync(properties); }
        catch (OperationCanceledException) when (Context.RequestAborted.IsCancellationRequested) { throw; }
        catch { Context.Response.Redirect("/login?authError=sign_in_failed"); }
    }
}
