using System.Security.Claims;
using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Xunit;

namespace Admin.WebApi.Tests.Authentication;

public sealed class AdminLogoutStoresTests
{
    [Fact]
    public async Task TicketTakeIsSingleWinnerAndRenewRemoveCannotRevive()
    {
        var time = new ManualOidcTime(); var store = new MemoryTicketStore(time);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "fake")], "test")), new(), AdminOidcSettings.SessionScheme);
        var key = await store.StoreAsync(ticket);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => store.Take(key))));
        Assert.Single(results.Where(t => t is not null)); Assert.Null(await store.RetrieveAsync(key));
        await store.RenewAsync(key, ticket); Assert.Null(store.Take(key));
        key = await store.StoreAsync(ticket); time.Advance(TimeSpan.FromHours(8)); Assert.Null(store.Take(key));
        Assert.Equal(0, store.Count);
        ticket.Properties.ExpiresUtc = null; key = await store.StoreAsync(ticket);
        await Task.WhenAll(Task.Run(() => store.RenewAsync(key, ticket)), Task.Run(() => store.RemoveAsync(key)), Task.Run(() => store.Take(key)));
        await store.RenewAsync(key, ticket); Assert.Null(await store.RetrieveAsync(key));
    }
    [Fact]
    public async Task LogoutStateIsBoundedBoundAtomicAndExpiresAtExactDeadline()
    {
        var time = new ManualOidcTime(); var store = new AdminLogoutStateStore(time);
        var binding = AdminLogoutStateStore.RandomKey(); var state = store.Create(binding)!;
        Assert.False(store.Consume(state, AdminLogoutStateStore.RandomKey())); Assert.Equal(1, store.Count);
        var consumers = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => store.Consume(state, binding)))); Assert.Single(consumers.Where(b => b));
        state = store.Create(binding)!; time.Advance(TimeSpan.FromMinutes(5)); Assert.False(store.Consume(state, binding)); Assert.Equal(0, store.Count);
        for (var i = 0; i < AdminLogoutStateStore.Capacity; i++) Assert.NotNull(store.Create(binding)); Assert.Null(store.Create(binding));
        time.Advance(TimeSpan.FromMinutes(5)); store.RemoveExpired(); Assert.Equal(0, store.Count); Assert.NotNull(store.Create(binding));
    }
    [Theory]
    [InlineData(false, false)][InlineData(false, true)][InlineData(true, false)]
    public void LogoutSwitchRequiresOidcAndSessionApi(bool enabled, bool api)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["AdminOidc:Enabled"] = enabled.ToString(), ["AdminOidc:UseSessionForAdminApi"] = api.ToString(), ["AdminOidc:UseSessionForLogout"] = "true" }).Build();
        Assert.Equal("AdminOidc:UseSessionForLogout requires AdminOidc:Enabled and AdminOidc:UseSessionForAdminApi",
            Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, new HostingEnvironment { EnvironmentName = "Production" })).Message);
    }
    [Theory]
    [InlineData("https://admin.example.test/api/auth/oidc/logout-callback", true)]
    [InlineData("https://external.example/api/auth/oidc/logout-callback", false)]
    [InlineData("https://admin.example.test/api/auth/oidc/logout-callback?state=bad", false)]
    [InlineData("https://admin.example.test/api/auth/oidc/logout-callback/", false)]
    [InlineData("https://admin.example.test/API/AUTH/OIDC/logout-callback", false)]
    [InlineData("http://admin.example.test/api/auth/oidc/logout-callback", false)]
    public void LogoutConfigurationRequiresExactSameOriginUri(string uri, bool valid)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdminOidc:Enabled"] = "true", ["AdminOidc:UseSessionForAdminApi"] = "true", ["AdminOidc:UseSessionForLogout"] = "true",
            ["AdminOidc:RedirectUri"] = "https://admin.example.test/api/auth/oidc/callback", ["AdminOidc:PostLogoutRedirectUri"] = uri,
            ["IdentityService:Authority"] = "https://identity.example.test", ["IdentityService:AppId"] = "fake-app", ["IdentityService:AppSecret"] = "fake-secret"
        }).Build();
        var env = new HostingEnvironment { EnvironmentName = "Production" };
        if (valid) Assert.Equal(uri, AdminOidcSettings.Read(config, env).PostLogoutRedirectUri);
        else Assert.Equal("AdminOidc:PostLogoutRedirectUri", Assert.Throws<InvalidOperationException>(() => AdminOidcSettings.Read(config, env)).Message);
    }
}
