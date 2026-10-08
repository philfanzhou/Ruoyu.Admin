using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using SignaCore.Client.AspNetCore;
using Xunit;

namespace Admin.WebApi.Tests.Authentication;

// The hosted-login protocol stores and endpoints are owned by the package; this file pins the
// Admin wiring consequences of the explicit intranet HTTP opt-in: the antiforgery cookie
// SecurePolicy follows the effective entry scheme, and the same canonical origin list satisfies
// the package's own options validation (resolving IOptions<SignaCoreHostedLoginOptions> runs it).
public sealed class AdminOidcRegistrationTests
{
    [Fact]
    public void IntranetHttpProfileUsesSameAsRequestCookiesAndPassesPackageValidation()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["IdentityService:Authority"] = "http://192.168.55.10:5002",
            ["AdminOidc:RedirectUri"] = "http://192.168.55.10:5020/api/auth/oidc/callback",
            ["AdminOidc:PostLogoutRedirectUri"] = "http://192.168.55.10:5020" + AdminOidcSettings.LogoutReturnPath,
            ["AdminOidc:IntranetHttpOrigins:0"] = "http://192.168.55.10:5002",
            ["AdminOidc:IntranetHttpOrigins:1"] = "http://192.168.55.10:5020",
        }, "Production");
        var antiforgery = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        Assert.Equal("adminCsrf", antiforgery.Cookie.Name);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, antiforgery.Cookie.SecurePolicy);
        // Resolving .Value runs the package's options validator: the canonical list must satisfy
        // it identically (grammar and duplicates) instead of failing the host later.
        var login = provider.GetRequiredService<IOptions<SignaCoreHostedLoginOptions>>().Value;
        Assert.Equal(new[] { "http://192.168.55.10:5002", "http://192.168.55.10:5020" }, login.IntranetHttpOrigins.Order().ToArray());
        Assert.Equal(AdminOidcSettings.SessionCookie, login.SessionCookieName); // no prefix: profile-compatible
    }

    [Fact]
    public void HttpsProfileKeepsAlwaysSecureCookiesAndEmptyPackageList()
    {
        var provider = BuildProvider(null, "Production");
        var antiforgery = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        Assert.Equal(CookieSecurePolicy.Always, antiforgery.Cookie.SecurePolicy);
        Assert.Empty(provider.GetRequiredService<IOptions<SignaCoreHostedLoginOptions>>().Value.IntranetHttpOrigins);
    }

    [Fact]
    public void LoopbackDevProfileKeepsSameAsRequestCookies()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["AdminOidc:RedirectUri"] = "http://127.0.0.1:5020/api/auth/oidc/callback",
            ["AdminOidc:PostLogoutRedirectUri"] = "http://127.0.0.1:5020" + AdminOidcSettings.LogoutReturnPath,
        }, "Development");
        var antiforgery = provider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        Assert.Equal(CookieSecurePolicy.SameAsRequest, antiforgery.Cookie.SecurePolicy);
        Assert.Empty(provider.GetRequiredService<IOptions<SignaCoreHostedLoginOptions>>().Value.IntranetHttpOrigins);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?>? overrides, string environment)
    {
        var values = AdminSessionDisabledTests.ValidConfiguration();
        if (overrides is not null)
            foreach (var (key, value) in overrides) values[key] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new HostingEnvironment { EnvironmentName = environment };
        var services = new ServiceCollection();
        // The package's options validator resolves the host environment the way a real host does.
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(env);
        services.AddSingleton<IConfiguration>(config);
        services.AddAdminOidc(config, env);
        return services.BuildServiceProvider();
    }
}
