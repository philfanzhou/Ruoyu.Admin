namespace Admin.WebApi.Authentication;

internal sealed record AdminOidcSettings(string Authority, string ClientId, string ClientSecret,
    string RedirectUri, bool InsecureLoopback, TimeSpan ClockSkew)
{
    internal string PostLogoutRedirectUri { get; init; } = "";
    internal const string LogoutCallbackPath = "/api/auth/oidc/logout-callback";
    public const string SessionScheme = "AdminSession";
    public const string OidcScheme = "AdminOidc";
    public const string CallbackPath = "/api/auth/oidc/callback";
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
        var dev = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var redirect = config["AdminOidc:RedirectUri"] ?? "";
        var authority = (config["IdentityService:Authority"] ?? "").TrimEnd('/');
        if (!IsSafeUri(redirect, dev, out var redirectUri) || redirectUri!.AbsolutePath != CallbackPath
            || redirectUri.AbsoluteUri != redirect || redirect.Length > 500 || redirect.Any(c => c > 127))
            throw new InvalidOperationException("AdminOidc:RedirectUri");
        if (!IsSafeUri(authority, dev, out _)) throw new InvalidOperationException("IdentityService:Authority");
        var postLogout = config["AdminOidc:PostLogoutRedirectUri"] ?? "";
        if (!IsSafeUri(postLogout, dev, out var postLogoutUri) || postLogoutUri!.AbsolutePath != LogoutCallbackPath
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
        return new(authority, clientId, secret, redirect, redirectUri.Scheme == "http", TimeSpan.FromSeconds(skew))
            { PostLogoutRedirectUri = postLogout };
    }

    internal static bool IsSafeUri(string value, bool allowLoopback, out Uri? uri)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out uri)
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
            && !value.Any(char.IsControl) && !value.Contains('*')
            && (uri.Scheme == "https" || allowLoopback && uri.Scheme == "http" && uri.Host is "127.0.0.1" or "[::1]");
    }

    internal static string ReturnPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 2048) return "/dashboard";
        // Reject encoded alternate origins and loop paths as well as their decoded forms.
        var candidate = path;
        for (var i = 0; i < 4; i++)
        {
            if (!candidate.StartsWith('/') || candidate.StartsWith("//") || candidate.Contains('\\')
                || candidate.Any(char.IsControl)) return "/dashboard";
            var route = candidate.Split('?', '#')[0];
            if (route.Split('/').Any(segment => segment is "." or "..")) return "/dashboard";
            if (route.Equals("/login", StringComparison.OrdinalIgnoreCase)
                || route.StartsWith("/login/", StringComparison.OrdinalIgnoreCase)
                || route.Equals("/api/auth", StringComparison.OrdinalIgnoreCase)
                || route.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)) return "/dashboard";
            var decoded = Uri.UnescapeDataString(candidate);
            if (decoded == candidate) return path;
            candidate = decoded;
        }
        return "/dashboard";
    }

    public override string ToString() => "AdminOidcSettings";
}
