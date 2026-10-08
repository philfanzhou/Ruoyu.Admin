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
            Assert.Equal(redirect.StartsWith("http:"), settings.InsecureHttp);
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

    [Theory]
    [InlineData("http://192.168.55.10:5020/api/auth/oidc/callback", "http://192.168.55.10:5020", true)]
    [InlineData("http://10.1.2.3:5020/api/auth/oidc/callback", "http://10.1.2.4:5020", false)]
    [InlineData("http://[fd00::10]:5020/api/auth/oidc/callback", "http://[FD00::10]:5020", true)]
    [InlineData("http://[fd00::10]:5020/api/auth/oidc/callback", "http://[fd00::11]:5020", false)]
    [InlineData("http://192.168.55.10:5020/api/auth/oidc/callback", "http://192.168.55.10:5002", false)]
    [InlineData("http://192.168.55.10:5020/api/auth/oidc/callback", "http://192.168.55.10:05020", true)]
    public void ConfigurationAdmitsExplicitIntranetHttpOriginsInEveryEnvironment(string redirect, string listed, bool valid)
    {
        var values = AdminSessionDisabledTests.ValidConfiguration();
        values["AdminOidc:RedirectUri"] = redirect;
        values["AdminOidc:PostLogoutRedirectUri"] = new Uri(redirect).GetLeftPart(UriPartial.Authority) + AdminOidcSettings.LogoutReturnPath;
        values["IdentityService:Authority"] = "http://192.168.55.10:5002";
        values["AdminOidc:IntranetHttpOrigins:0"] = listed;
        if (!string.Equals(listed, "http://192.168.55.10:5002", StringComparison.Ordinal))
            values["AdminOidc:IntranetHttpOrigins:1"] = "http://192.168.55.10:5002";
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };
        if (valid)
        {
            var settings = AdminOidcSettings.Read(config, env);
            Assert.True(settings.InsecureHttp);
            Assert.Contains(new Uri(redirect).GetLeftPart(UriPartial.Authority), settings.IntranetHttpOrigins);
            Assert.Contains("http://192.168.55.10:5002", settings.IntranetHttpOrigins);
        }
        else Assert.Equal("AdminOidc:RedirectUri", Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env)).Message);
    }

    [Theory]
    [InlineData("http://192.168.55.10:5002", true)]
    [InlineData("http://192.168.55.11:5002", false)]
    [InlineData(null, false)]
    public void AuthoritySurfaceAdmitsOnlyListedIntranetHttpOrigin(string? listed, bool valid)
    {
        var values = AdminSessionDisabledTests.ValidConfiguration();
        values["IdentityService:Authority"] = "http://192.168.55.10:5002";
        if (listed is not null) values["AdminOidc:IntranetHttpOrigins:0"] = listed;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };
        if (valid)
        {
            var settings = AdminOidcSettings.Read(config, env);
            Assert.Equal("http://192.168.55.10:5002", settings.Authority);
            Assert.False(settings.InsecureHttp); // the HTTPS redirect entry keeps Secure cookies
        }
        else Assert.Equal("IdentityService:Authority", Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env)).Message);
    }

    [Theory]
    [InlineData("http://192.168.055.10:5020")]
    [InlineData("http://[fe80::1]:5020")]
    [InlineData("http://[::ffff:192.168.55.10]:5020")]
    [InlineData("http://192.168.55.10:0")]
    [InlineData("http://192.168.55.10:65536")]
    [InlineData("http://192.168.55.10:5020/api")]
    [InlineData("http://192.168.55.10:5020?x=1")]
    [InlineData("https://192.168.55.10:5020")]
    [InlineData("http://8.8.8.8:5020")]
    [InlineData("http://172.32.0.1:5020")]
    [InlineData("http://admin.intranet.test:5020")]
    [InlineData("http://192.168.55.10")]
    [InlineData(" http://192.168.55.10:5020")]
    [InlineData("http://user@192.168.55.10:5020")]
    [InlineData("http://192.168.55.10:5020%20")]
    [InlineData("")]
    [InlineData("http://127.0.0.1:5020")]
    public void ConfigurationRejectsMalformedIntranetHttpOriginListEntries(string entry)
    {
        var values = AdminSessionDisabledTests.ValidConfiguration();
        values["AdminOidc:IntranetHttpOrigins:0"] = entry;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };
        var error = Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env));
        Assert.Equal("AdminOidc:IntranetHttpOrigins", error.Message);
        if (entry.Length > 0) Assert.DoesNotContain(entry, error.ToString()); // key only, never a value
    }

    [Fact]
    public void ConfigurationRejectsDuplicateIntranetHttpOriginsAfterNormalization()
    {
        var values = AdminSessionDisabledTests.ValidConfiguration();
        values["AdminOidc:IntranetHttpOrigins:0"] = "http://192.168.55.10:5020";
        values["AdminOidc:IntranetHttpOrigins:1"] = "http://192.168.55.10:05020";
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };
        Assert.Equal("AdminOidc:IntranetHttpOrigins",
            Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env)).Message);
    }

    [Fact]
    public void ConfigurationRejectsScalarShapedIntranetHttpOriginList()
    {
        var values = AdminSessionDisabledTests.ValidConfiguration();
        values["AdminOidc:IntranetHttpOrigins"] = "http://192.168.55.10:5020";
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var env = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };
        Assert.Equal("AdminOidc:IntranetHttpOrigins",
            Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env)).Message);
    }

    [Fact]
    public void MissingListKeepsHttpsOnlyEnforcementAndEmptyCanonicalSet()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(AdminSessionDisabledTests.ValidConfiguration()).Build();
        var settings = AdminOidcSettings.Read(config, new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" });
        Assert.Empty(settings.IntranetHttpOrigins);
        Assert.False(settings.InsecureHttp);
    }
}
