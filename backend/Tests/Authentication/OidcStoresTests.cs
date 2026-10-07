using Admin.WebApi.Authentication;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Admin.WebApi.Tests.Authentication;

// The hosted-login protocol stores (pending sign-in, ticket store, logout-return state) are
// owned and tested by SignaCore.Client.AspNetCore; this file keeps the Admin configuration
// contract tests of the startup boundary.
internal sealed class ManualOidcTime : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    internal void Advance(TimeSpan span) => _now += span;
}

public sealed class OidcStoresTests
{
    [Theory]
    [InlineData("https://admin.example.test/api/auth/oidc/callback", "Production", true)]
    [InlineData("http://127.0.0.1:5020/api/auth/oidc/callback", "Development", true)]
    [InlineData("http://[::1]:5020/api/auth/oidc/callback", "Testing", true)]
    [InlineData("http://127.0.0.1:5020/api/auth/oidc/callback", "Production", false)]
    [InlineData("http://localhost:5020/api/auth/oidc/callback", "Development", false)]
    [InlineData("https://localhost/api/auth/oidc/callback", "Production", false)]
    [InlineData("https://LOCALHOST/api/auth/oidc/callback", "Testing", false)]
    [InlineData("https://localhost./api/auth/oidc/callback", "Development", false)]
    public void ConfigurationEnforcesEnvironmentTlsAndNumericLoopback(string redirect, string environment, bool valid)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdminOidc:RedirectUri"] = redirect,
            ["AdminOidc:PostLogoutRedirectUri"] = new Uri(redirect).GetLeftPart(UriPartial.Authority) + AdminOidcSettings.LogoutReturnPath,
            ["IdentityService:Authority"] = "https://identity.example.test/", ["IdentityService:AppId"] = "fake-app", ["IdentityService:AppSecret"] = "fake-secret"
        }).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = environment };
        if (valid)
        {
            var settings = AdminOidcSettings.Read(config, env);
            Assert.Equal("https://identity.example.test", settings.Authority);
            Assert.Equal(redirect.StartsWith("http:"), settings.InsecureLoopback);
        }
        else Assert.Equal("AdminOidc:RedirectUri", Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env)).Message);
    }

    [Theory]
    [InlineData("IdentityService:Authority", "https://localhost")]
    [InlineData("AdminOidc:PostLogoutRedirectUri", "https://localhost" + AdminOidcSettings.LogoutReturnPath)]
    [InlineData("AdminOidc:PostLogoutRedirectUri", "https://admin.example.test/api/auth/oidc/logout-callback")]
    public void ConfigurationRejectsHttpsLocalhostAndLegacyLogoutCallbackInEveryUri(string key, string value)
    {
        var values = AdminSessionDisabledTests.ValidConfiguration();
        values[key] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };
        Assert.Equal(key, Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env)).Message);
    }
}
