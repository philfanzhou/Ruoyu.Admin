namespace Admin.WebApi.Authentication;

internal sealed record AdminOidcSettings(bool Enabled, string Authority, string ClientId, string ClientSecret,
    string RedirectUri, bool InsecureLoopback, TimeSpan ClockSkew)
{
    internal bool UseSessionForAdminApi { get; init; }
    internal bool UseSessionForPortalProxies { get; init; }
    internal bool UseSessionForIdentityProxy { get; init; }
    public const string SessionScheme = "AdminSession";
    public const string OidcScheme = "AdminOidc";
    public const string CallbackPath = "/api/auth/oidc/callback";
    public const string SessionCookie = "adminSession";

    internal static AdminOidcSettings Read(IConfiguration config, IHostEnvironment environment)
    {
        var useSession = config.GetValue<bool>("AdminOidc:UseSessionForAdminApi");
        var portalProxies = config.GetValue<bool>("AdminOidc:UseSessionForPortalProxies");
        if (portalProxies && (!useSession || !config.GetValue<bool>("AdminOidc:Enabled")))
            throw new InvalidOperationException("AdminOidc:UseSessionForPortalProxies requires AdminOidc:Enabled and AdminOidc:UseSessionForAdminApi");
        var identityProxy = config.GetValue<bool>("AdminOidc:UseSessionForIdentityProxy");
        if (identityProxy && (!useSession || !config.GetValue<bool>("AdminOidc:Enabled")))
            throw new InvalidOperationException("AdminOidc:UseSessionForIdentityProxy requires AdminOidc:Enabled and AdminOidc:UseSessionForAdminApi");
        if (!config.GetValue<bool>("AdminOidc:Enabled"))
        {
            if (useSession) throw new InvalidOperationException("AdminOidc:UseSessionForAdminApi requires AdminOidc:Enabled");
            return new(false, "", "", "", "", false, TimeSpan.Zero);
        }
        var dev = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        var redirect = config["AdminOidc:RedirectUri"] ?? "";
        var authority = (config["IdentityService:Authority"] ?? "").TrimEnd('/');
        if (!IsSafeUri(redirect, dev, out var redirectUri) || redirectUri!.AbsolutePath != CallbackPath
            || redirectUri.AbsoluteUri != redirect || redirect.Length > 500 || redirect.Any(c => c > 127))
            throw new InvalidOperationException("AdminOidc:RedirectUri");
        if (!IsSafeUri(authority, dev, out _)) throw new InvalidOperationException("IdentityService:Authority");
        var clientId = config["IdentityService:AppId"];
        var secret = config["IdentityService:AppSecret"];
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidOperationException("IdentityService:AppId");
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException("IdentityService:AppSecret");
        var skew = config.GetValue<int?>("IdentityService:ClockSkewSeconds") ?? 30;
        if (skew is < 0 or > 300) throw new InvalidOperationException("IdentityService:ClockSkewSeconds");
        return new(true, authority, clientId, secret, redirect, redirectUri.Scheme == "http", TimeSpan.FromSeconds(skew))
            { UseSessionForAdminApi = useSession, UseSessionForPortalProxies = portalProxies, UseSessionForIdentityProxy = identityProxy };
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
