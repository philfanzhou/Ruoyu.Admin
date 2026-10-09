using Admin.WebApi.Authentication;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Admin.WebApi.Tests.Authentication;

// The hosted-login protocol stores (pending sign-in, ticket store, logout-return state) are
// owned and tested by SignaCore.Client.AspNetCore; this file keeps the Admin configuration
// contract tests of the startup boundary.
internal sealed class ManualOidcTime : TimeProvider
{
    // The clock keeps ticking in real time and only carries a manual forward shift: the test
    // authority mints its tokens at the real current instant, and SignaCore.Client.AspNetCore
    // 0.1.14+ validates token iat against the host TimeProvider with zero clock skew by
    // default, so a clock frozen at construction would make every minted token look issued in
    // the future and fail the sign-in. Advance() keeps its time-travel semantics.
    private TimeSpan _shift;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _shift;
    internal void Advance(TimeSpan span) => _shift += span;
}

public sealed class OidcStoresTests
{
    [Theory]
    [InlineData("https://admin.example.test/api/auth/oidc/callback", "Production", true)]
    [InlineData("http://127.0.0.1:5020/api/auth/oidc/callback", "Development", true)]
    [InlineData("http://[::1]:5020/api/auth/oidc/callback", "Testing", true)]
    // Transport security is a deployment decision (issue #94): http is accepted equally in
    // every environment name, for any host shape except the rejected "localhost" name.
    [InlineData("http://127.0.0.1:5020/api/auth/oidc/callback", "Production", true)]
    [InlineData("http://192.168.55.10:5020/api/auth/oidc/callback", "Production", true)]
    [InlineData("http://[fd00::10]:5020/api/auth/oidc/callback", "Production", true)]
    [InlineData("http://admin.example.test:5020/api/auth/oidc/callback", "Production", true)]
    [InlineData("http://192.168.55.10:5020/api/auth/oidc/callback", "Testing", true)]
    [InlineData("http://localhost:5020/api/auth/oidc/callback", "Development", false)]
    [InlineData("https://localhost/api/auth/oidc/callback", "Production", false)]
    [InlineData("https://LOCALHOST/api/auth/oidc/callback", "Testing", false)]
    [InlineData("https://localhost./api/auth/oidc/callback", "Development", false)]
    public void ConfigurationAcceptsHttpAndHttpsEquallyInEveryEnvironment(string redirect, string environment, bool valid)
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
            Assert.Equal(redirect.StartsWith("http:"), settings.InsecureHttp);
        }
        else Assert.Equal("AdminOidc:RedirectUri", Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env)).Message);
    }

    [Theory]
    [InlineData("IdentityService:Authority", "https://localhost")]
    [InlineData("IdentityService:Authority", "http://localhost:5002")]
    [InlineData("AdminOidc:PostLogoutRedirectUri", "https://localhost" + AdminOidcSettings.LogoutReturnPath)]
    [InlineData("AdminOidc:PostLogoutRedirectUri", "http://localhost:5020" + AdminOidcSettings.LogoutReturnPath)]
    [InlineData("AdminOidc:PostLogoutRedirectUri", "https://admin.example.test/api/auth/oidc/logout-callback")]
    public void ConfigurationRejectsLocalhostHostsAndLegacyLogoutCallbackInEveryUri(string key, string value)
    {
        var values = AdminSessionDisabledTests.ValidConfiguration();
        values[key] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };
        Assert.Equal(key, Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env)).Message);
    }

    [Theory]
    [InlineData("http://192.168.55.10:5002")]
    [InlineData("http://10.1.2.3:5002")]
    [InlineData("http://[fd12::34]:5002")]
    [InlineData("http://identity.example.test:5002")]
    [InlineData("https://identity.example.test")]
    public void AuthoritySurfaceAcceptsHttpAndHttpsEquallyWithoutAnyList(string authority)
    {
        var values = AdminSessionDisabledTests.ValidConfiguration();
        values["IdentityService:Authority"] = authority;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };
        var settings = AdminOidcSettings.Read(config, env);
        Assert.Equal(authority, settings.Authority);
        Assert.False(settings.InsecureHttp); // the HTTPS redirect entry keeps Secure cookies
    }

    [Theory]
    [InlineData("AdminOidc:RedirectUri", "https://user@admin.example.test/api/auth/oidc/callback")]
    [InlineData("AdminOidc:RedirectUri", "http://user@192.168.55.10:5020/api/auth/oidc/callback")]
    [InlineData("AdminOidc:RedirectUri", "https://admin.example.test/api/auth/oidc/callback?x=1")]
    [InlineData("AdminOidc:RedirectUri", "http://192.168.55.10:5020/api/auth/oidc/callback?x=1")]
    [InlineData("AdminOidc:RedirectUri", "https://admin.example.test/api/auth/oidc/callback#f")]
    [InlineData("AdminOidc:RedirectUri", "http://192.168.55.10:5020/api/auth/oidc/callback#f")]
    [InlineData("AdminOidc:RedirectUri", "https://*.example.test/api/auth/oidc/callback")]
    [InlineData("AdminOidc:RedirectUri", "http://192.168.55.10:5020/api/auth/oidc/Callback")]
    [InlineData("AdminOidc:RedirectUri", "http://192.168.55.10:05020/api/auth/oidc/callback")]
    [InlineData("AdminOidc:RedirectUri", "ftp://192.168.55.10:5020/api/auth/oidc/callback")]
    [InlineData("AdminOidc:RedirectUri", "http://192.168.55.10:5020/api/auth/oidc/callbacké")]
    [InlineData("AdminOidc:RedirectUri", "http://192.168.55.10:5020/api/auth/oidc/callback\u0008")]
    [InlineData("IdentityService:Authority", "ftp://192.168.55.10:5002")]
    [InlineData("IdentityService:Authority", "http://user@192.168.55.10:5002")]
    public void ConfigurationRejectsStructuralUriViolationsInBothSchemes(string key, string value)
    {
        var values = AdminSessionDisabledTests.ValidConfiguration();
        values[key] = value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };
        var error = Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env));
        Assert.Equal(key, error.Message);
        Assert.DoesNotContain(value, error.ToString()); // key only, never a value
    }

    [Fact]
    public void ConfigurationRejectsRedirectsLongerThanFiveHundredCharacters()
    {
        // Path and spelling stay exactly canonical so only the length rule rejects.
        var tooLong = "https://" + new string('a', 500) + ".test/api/auth/oidc/callback";
        Assert.True(tooLong.Length > 500);
        var values = AdminSessionDisabledTests.ValidConfiguration();
        values["AdminOidc:RedirectUri"] = tooLong;
        values["AdminOidc:PostLogoutRedirectUri"] = "https://" + new string('a', 500) + ".test" + AdminOidcSettings.LogoutReturnPath;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };
        Assert.Equal("AdminOidc:RedirectUri", Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env)).Message);
    }

    [Fact]
    public void ResidualRetiredIntranetHttpOriginKeyIsNotRead()
    {
        // Leftover entries of the retired 0.1.15 opt-in list — even ones its grammar would
        // have rejected — never reach the parser and never block startup.
        var values = AdminSessionDisabledTests.ValidConfiguration();
        values["AdminOidc:IntranetHttpOrigins:0"] = "not-even-a-uri";
        values["AdminOidc:IntranetHttpOrigins:1"] = "http://192.168.55.10:5020";
        values["AdminOidc:IntranetHttpOrigins"] = "http://192.168.55.10:5020";
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var settings = AdminOidcSettings.Read(config, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" });
        Assert.Equal("https://admin.example.test/api/auth/oidc/callback", settings.RedirectUri);
        Assert.False(settings.InsecureHttp);
    }
}
