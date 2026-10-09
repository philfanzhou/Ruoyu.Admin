namespace Admin.WebApi.Authentication;

internal sealed record AdminOidcSettings(string Authority, string ClientId, string ClientSecret,
    string RedirectUri, bool InsecureHttp, TimeSpan ClockSkew)
{
    internal string PostLogoutRedirectUri { get; init; } = "";
    // The SignaCore client package mounts its endpoints at a fixed prefix and requires the
    // registered redirect URIs to match <prefix>/callback and <prefix>/logout/return exactly.
    public const string HostedLoginPrefix = "/api/auth/oidc";
    public const string CallbackPath = HostedLoginPrefix + "/callback";
    public const string LogoutReturnPath = HostedLoginPrefix + "/logout/return";
    public const string LogoutPath = HostedLoginPrefix + "/logout";
    public const string SessionCookie = "adminSession";

    internal static AdminOidcSettings Read(IConfiguration config, IHostEnvironment environment)
    {
        // Migration validation only: these keys can never select a runtime mode.
        foreach (var key in new[] { "Enabled", "UseSessionForAdminApi", "UseSessionForLogout",
            "UseSessionForIdentityProxy", "UseSessionForPortalProxies" })
        {
            var section = config.GetSection("AdminOidc:" + key);
            // JsonConfigurationProvider normalizes the JSON boolean true to "True".
            // Accept that representation and the canonical environment/cache string only.
            if ((section.Exists() || config.AsEnumerable().Any(entry =>
                    string.Equals(entry.Key, section.Path, StringComparison.OrdinalIgnoreCase)))
                && section.Value is not ("true" or "True"))
                throw new InvalidOperationException("AdminOidc:" + key);
        }
        // Transport security is a deployment decision (issue #94, aligned with SignaCore #566):
        // http and https are accepted equally in every environment name by each IsSafeUri call
        // below (redirect, authority, post-logout) and by MistakeSessionSettings.Read; only the
        // structural URI rules remain in code. A residual AdminOidc:IntranetHttpOrigins key
        // (the retired 0.1.15 opt-in list) is not read and never blocks startup.
        var redirect = config["AdminOidc:RedirectUri"] ?? "";
        var authority = (config["IdentityService:Authority"] ?? "").TrimEnd('/');
        if (!IsSafeUri(redirect, out var redirectUri) || redirectUri!.AbsolutePath != CallbackPath
            || redirectUri.AbsoluteUri != redirect || redirect.Length > 500 || redirect.Any(c => c > 127))
            throw new InvalidOperationException("AdminOidc:RedirectUri");
        if (!IsSafeUri(authority, out _)) throw new InvalidOperationException("IdentityService:Authority");
        var postLogout = config["AdminOidc:PostLogoutRedirectUri"] ?? "";
        if (!IsSafeUri(postLogout, out var postLogoutUri) || postLogoutUri!.AbsolutePath != LogoutReturnPath
            || postLogoutUri.AbsoluteUri != postLogout || postLogout.Length > 500 || postLogout.Any(c => c > 127)
            || postLogoutUri.GetLeftPart(UriPartial.Authority) != redirectUri.GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("AdminOidc:PostLogoutRedirectUri");
        var clientId = config["IdentityService:AppId"];
        var secret = config["IdentityService:AppSecret"];
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException("IdentityService:AppId");
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("IdentityService:AppSecret");
        var skewText = config["IdentityService:ClockSkewSeconds"];
        var skew = 30;
        if (skewText is not null && !int.TryParse(skewText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out skew))
            throw new InvalidOperationException("IdentityService:ClockSkewSeconds");
        if (skew is < 0 or > 300) throw new InvalidOperationException("IdentityService:ClockSkewSeconds");
        // ClockSkew stays part of the startup contract (malformed values still fail the host);
        // the SignaCore client package validates its own token lifetimes with the strict default
        // (zero clock skew, future iat rejected) since 0.1.14 and this repository does not relax it.
        // InsecureHttp reflects "the configured entry origin is plain HTTP", which drives the
        // adminCsrf cookie SecurePolicy below.
        return new(authority, clientId, secret, redirect, redirectUri.Scheme == "http", TimeSpan.FromSeconds(skew))
            { PostLogoutRedirectUri = postLogout };
    }

    internal static bool IsSafeUri(string value, out Uri? uri)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out uri)
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
            && !string.Equals(uri.Host.TrimEnd('.'), "localhost", StringComparison.OrdinalIgnoreCase)
            && !value.Any(char.IsControl) && !value.Contains('*')
            && uri!.Scheme is "http" or "https";
    }

    public override string ToString() => "AdminOidcSettings";
}
