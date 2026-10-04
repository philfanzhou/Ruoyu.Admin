using System.Security.Claims;
using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Admin.WebApi.Tests.Authentication;

internal sealed class ManualOidcTime : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    internal void Advance(TimeSpan span) => _now += span;
}

public sealed class OidcStoresTests
{
    [Fact]
    public async Task State_IsCompactSingleUseAtomicBoundedAndExpiring()
    {
        var time = new ManualOidcTime();
        var store = new CompactStateDataFormat(time);
        var key = store.Protect(new AuthenticationProperties { RedirectUri = "/safe" });
        Assert.Matches("^[A-Za-z0-9_-]{43}$", key);
        var consumers = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => store.Unprotect(key))));
        Assert.Single(consumers.Where(result => result is not null));
        key = store.Protect(new());
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(store.Unprotect(key));
        Assert.Equal(0, store.Count);
        for (var i = 0; i < CompactStateDataFormat.Capacity; i++) store.Protect(new());
        Assert.Equal("oidc.state_capacity", Assert.Throws<InvalidOperationException>(() => store.Protect(new())).Message);
        time.Advance(TimeSpan.FromMinutes(5));
        store.RemoveExpired();
        Assert.Equal(0, store.Count);
        Assert.NotNull(store.Protect(new()));
    }

    [Fact]
    public async Task Ticket_IsSnapshotAbsoluteExpiredRemovedAndRenewCannotResurrect()
    {
        var time = new ManualOidcTime();
        var store = new MemoryTicketStore(time);
        var ticket = Ticket();
        var key = await store.StoreAsync(ticket);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", key);
        ticket.Properties.Items["canary"] = "modified-after-store";
        Assert.False((await store.RetrieveAsync(key))!.Properties.Items.ContainsKey("canary"));
        var deadline = (await store.RetrieveAsync(key))!.Properties.ExpiresUtc;
        time.Advance(TimeSpan.FromHours(1));
        ticket.Properties.ExpiresUtc = time.GetUtcNow() + TimeSpan.FromHours(8);
        await store.RenewAsync(key, ticket);
        Assert.Equal(deadline, (await store.RetrieveAsync(key))!.Properties.ExpiresUtc);
        await Task.WhenAll(Task.Run(() => store.RemoveAsync(key)), Task.Run(() => store.RenewAsync(key, ticket)));
        Assert.Null(await store.RetrieveAsync(key));
        await store.RenewAsync(key, ticket);
        Assert.Null(await store.RetrieveAsync(key));
        key = await store.StoreAsync(Ticket());
        time.Advance(TimeSpan.FromHours(8));
        Assert.Null(await store.RetrieveAsync(key));
        Assert.Equal(0, store.Count);
        var fresh = new MemoryTicketStore(time);
        Assert.Null(await fresh.RetrieveAsync(await store.StoreAsync(Ticket())));
        var cancellation = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.StoreAsync(Ticket(), new Microsoft.AspNetCore.Http.DefaultHttpContext(), cancellation));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task TicketCapacityIsBoundedAndSweepReleasesCapacity()
    {
        var time = new ManualOidcTime();
        var store = new MemoryTicketStore(time);
        for (var i = 0; i < MemoryTicketStore.Capacity; i++) await store.StoreAsync(Ticket());
        Assert.Equal("oidc.session_capacity", (await Assert.ThrowsAsync<InvalidOperationException>(() => store.StoreAsync(Ticket()))).Message);
        time.Advance(TimeSpan.FromHours(8));
        store.RemoveExpired();
        Assert.Equal(0, store.Count);
        Assert.NotNull(await store.StoreAsync(Ticket()));
    }

    [Theory]
    [InlineData("https://admin.example.test/api/auth/oidc/callback", "Production", true)]
    [InlineData("http://127.0.0.1:5020/api/auth/oidc/callback", "Development", true)]
    [InlineData("http://[::1]:5020/api/auth/oidc/callback", "Testing", true)]
    [InlineData("http://127.0.0.1:5020/api/auth/oidc/callback", "Production", false)]
    [InlineData("http://localhost:5020/api/auth/oidc/callback", "Development", false)]
    public void ConfigurationEnforcesEnvironmentTlsAndNumericLoopback(string redirect, string environment, bool valid)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdminOidc:RedirectUri"] = redirect,
            ["AdminOidc:PostLogoutRedirectUri"] = new Uri(redirect).GetLeftPart(UriPartial.Authority) + "/api/auth/oidc/logout-callback",
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
    [InlineData("//external.example")]
    [InlineData("https://external.example")]
    [InlineData("/%2fexternal.example")]
    [InlineData("/%255cexternal.example")]
    [InlineData("/api/auth/oidc/start")]
    [InlineData("/%61pi/auth/session")]
    [InlineData("/login?return=foo")]
    [InlineData("/safe%0d%0aLocation:external")]
    [InlineData("/safe/../login")]
    [InlineData("/safe/%2e%2e/api/auth/session")]
    public void ReturnPathsRejectExternalEncodedAndLoopTargets(string value) => Assert.Equal("/dashboard", AdminOidcSettings.ReturnPath(value));

    [Fact]
    public void ReturnPathAllowsAbsoluteLocalRoute() => Assert.Equal("/students?page=2", AdminOidcSettings.ReturnPath("/students?page=2"));

    private static AuthenticationTicket Ticket() => new(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "fake")], "test")), new(), AdminOidcSettings.SessionScheme);
}
