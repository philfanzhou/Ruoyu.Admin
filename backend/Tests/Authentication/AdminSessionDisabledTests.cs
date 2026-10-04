using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Xunit;

namespace Admin.WebApi.Tests.Authentication;

public sealed class AdminSessionDisabledTests
{
    internal static Dictionary<string, string?> ValidConfiguration() => new()
    {
        ["IdentityService:Authority"] = "https://identity.example.test",
        ["IdentityService:AppId"] = "fixture-app", ["IdentityService:AppSecret"] = "fixture-secret",
        ["AdminOidc:RedirectUri"] = "https://admin.example.test/api/auth/oidc/callback",
        ["AdminOidc:PostLogoutRedirectUri"] = "https://admin.example.test/api/auth/oidc/logout-callback"
    };

    public static IEnumerable<object?[]> LegacyValues()
    {
        foreach (var key in new[] { "Enabled", "UseSessionForAdminApi", "UseSessionForLogout", "UseSessionForIdentityProxy", "UseSessionForPortalProxies" })
        foreach (var value in new[] { "false", "False", "TRUE", "1", "", "fixture-secret-canary", null })
            yield return ["AdminOidc:" + key, value];
    }

    [Theory]
    [MemberData(nameof(LegacyValues))]
    public void LegacyFalseOrMalformedRejectsInEveryEnvironmentWithoutValues(string key, string? value)
    {
        foreach (var environment in new[] { "Production", "Development", "Testing" })
        {
            var values = ValidConfiguration(); values[key] = value;
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var error = Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, new HostingEnvironment { EnvironmentName = environment }));
            Assert.Equal(key, error.Message);
        }
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void AbsentOrCanonicalTrueAlwaysUsesCompleteConfiguration(bool legacyTrue)
    {
        var values = ValidConfiguration();
        if (legacyTrue) foreach (var key in new[] { "Enabled", "UseSessionForAdminApi", "UseSessionForLogout", "UseSessionForIdentityProxy", "UseSessionForPortalProxies" }) values["AdminOidc:" + key] = "true";
        var settings = AdminOidcSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new HostingEnvironment { EnvironmentName = "Production" });
        Assert.Equal(values["AdminOidc:PostLogoutRedirectUri"], settings.PostLogoutRedirectUri);
        Assert.Equal(values["IdentityService:Authority"], settings.Authority);
        Assert.DoesNotContain("fixture-secret", settings.ToString());
    }

    [Theory]
    [InlineData("IdentityService:Authority")][InlineData("IdentityService:AppId")][InlineData("IdentityService:AppSecret")]
    [InlineData("AdminOidc:RedirectUri")][InlineData("AdminOidc:PostLogoutRedirectUri")]
    public void EveryRequiredSettingFailsBeforeHostWhenMissing(string key)
    {
        var values = ValidConfiguration(); values.Remove(key);
        Assert.Equal(key, Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new HostingEnvironment { EnvironmentName = "Production" })).Message);
    }

    [Theory]
    [InlineData("GET")][InlineData("HEAD")][InlineData("PUT")][InlineData("PATCH")]
    [InlineData("DELETE")][InlineData("OPTIONS")][InlineData("TRACE")]
    public async Task LogoutRejectsOtherMethodsBeforeRevocation(string method)
    {
        var middleware = new AdminLogoutMiddleware(_ => throw new InvalidOperationException("Must not fall through"));
        var context = new DefaultHttpContext(); context.Request.Path = "/API/AUTH/LOGOUT/"; context.Request.Method = method;
        await middleware.InvokeAsync(context, null!);
        Assert.Equal(405, context.Response.StatusCode);
    }
}
