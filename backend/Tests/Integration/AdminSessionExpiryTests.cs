using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Admin.WebApi.Authentication;
using Admin.WebApi.Tests.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Ruoyu.Admin.Common.Oss;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

public sealed partial class AdminOidcTests
{
    [Fact]
    public async Task SessionExpiry_DeadlineStatusConcurrentWritesAndAbsoluteTicketLifetimeAgree()
    {
        using var authority = new OidcTestAuthority(); var time = new ManualOidcTime(); var probe = new SessionBusinessProbe();
        using var factory = SessionFactory(authority, probe, time); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var csrf = await Csrf(client, cookie);
        var (key, ticket) = await Stored(factory, cookie); var originalIdToken = ticket.Properties.GetTokenValue("id_token");
        var deadline = time.GetUtcNow().AddMinutes(1);
        ticket.Properties.UpdateTokenValue("expires_at", deadline.ToString("o", CultureInfo.InvariantCulture));
        await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
        time.Advance(TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1));
        Assert.Equal("{\"authenticated\":true,\"displayName\":\"fake-display\",\"requiresReauthentication\":false}", await Status(client, cookie));
        Assert.Equal(HttpStatusCode.OK, (await Api(client, "/api/admin/session-probe", cookie)).StatusCode);
        time.Advance(TimeSpan.FromTicks(1));
        foreach (var offset in new[] { TimeSpan.Zero, TimeSpan.FromTicks(1) })
        {
            time.Advance(offset);
            Assert.Equal("{\"authenticated\":false,\"displayName\":\"fake-display\",\"requiresReauthentication\":true}", await Status(client, cookie));
            var requests = Enumerable.Range(0, 8).Select(_ => Api(client, "/api/admin/oss-audit/records/1/resolve", cookie + "; " + csrf.Cookie, "POST", csrf: [csrf.Token]));
            foreach (var response in await Task.WhenAll(requests)) await Rejected(response, 401, "reauthentication_required");
            await Rejected(await Api(client, "/api/admin/session-probe", cookie), 401, "reauthentication_required");
            await Rejected(await Api(client, "/api/auth/csrf", cookie), 401, "reauthentication_required");
        }
        probe.Oss.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
        Assert.Equal(1, probe.Reads); Assert.Equal(0, probe.Writes); Assert.Equal(0, probe.Identity.Calls);
        Assert.Single(authority.TokenForms); Assert.Equal("authorization_code", authority.TokenForms.Single()["grant_type"]);
        Assert.Equal(originalIdToken, (await Stored(factory, cookie)).Ticket.Properties.GetTokenValue("id_token"));
        time.Advance(TimeSpan.FromHours(8));
        Assert.Equal("{\"authenticated\":false,\"displayName\":null,\"requiresReauthentication\":false}", await Status(client, cookie));
        await Rejected(await Api(client, "/api/admin/session-probe", cookie), 401, "unauthorized");
    }

    [Theory]
    [InlineData("anonymous")][InlineData("revoked")][InlineData("missing-token")][InlineData("invalid-time")][InlineData("no-offset")][InlineData("bad-stamp")]
    public async Task SessionExpiry_UntrustedOrIncompleteTicketsNeverAdvertiseReauthentication(string defect)
    {
        using var authority = new OidcTestAuthority(); var probe = new SessionBusinessProbe();
        using var factory = SessionFactory(authority, probe); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var (key, ticket) = await Stored(factory, cookie);
        var store = factory.Services.GetRequiredService<MemoryTicketStore>();
        if (defect == "anonymous") cookie = null!;
        else if (defect == "revoked") await store.RemoveAsync(key);
        else
        {
            if (defect == "missing-token") ticket.Properties.StoreTokens(ticket.Properties.GetTokens().Where(t => t.Name != "access_token"));
            if (defect == "invalid-time") ticket.Properties.UpdateTokenValue("expires_at", "tomorrow");
            if (defect == "no-offset") ticket.Properties.UpdateTokenValue("expires_at", "2099-01-01T00:00:00.0000000");
            if (defect == "bad-stamp") ticket.Properties.Items["oidc.subject"] = "wrong";
            await store.RenewAsync(key, ticket);
        }
        Assert.Equal("{\"authenticated\":false,\"displayName\":null,\"requiresReauthentication\":false}", await Status(client, cookie));
        await Rejected(await Api(client, "/api/admin/session-probe", cookie), 401, "unauthorized");
        Assert.Equal(0, probe.Reads);
    }

    [Fact]
    public async Task SessionExpiry_StatusRejectsHeadersAndRemovedAdminsBeforeExpiredToken()
    {
        using var authority = new OidcTestAuthority(); var probe = new SessionBusinessProbe();
        using var factory = SessionFactory(authority, probe); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var (key, ticket) = await Stored(factory, cookie);
        ticket.Properties.UpdateTokenValue("expires_at", DateTimeOffset.UtcNow.AddHours(-1).ToString("o"));
        await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
        var bearerBeforeStatus = probe.BearerAuthentications;
        probe.Admins.CurrentValue.AdminUserIds.Clear();
        await Rejected(await Api(client, "/api/auth/session", cookie), 403, "forbidden");
        await Rejected(await Api(client, "/api/admin/session-probe", cookie), 403, "forbidden");
        foreach (var header in new[] { new[] { "Bearer wrong" }, new[] { "Bearer " + authority.LegacyBearer() }, new[] { "a", "b" } })
            await Rejected(await Api(client, "/api/auth/session", cookie, authorization: header), 401, "unauthorized");
        var empty = await factory.Server.SendAsync(ctx =>
        { ctx.Request.Path = "/api/auth/session"; ctx.Request.Method = "GET"; ctx.Request.Scheme = "https"; ctx.Request.Headers.Cookie = cookie; ctx.Request.Headers.Authorization = ""; });
        Assert.Equal(401, empty.Response.StatusCode);
        Assert.Equal(bearerBeforeStatus, probe.BearerAuthentications);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task SessionExpiry_ExplicitReauthenticationAcceptsBrowserReturnAfterReusableOrLoginFlow(bool reusable)
    {
        using var authority = new OidcTestAuthority(); var probe = new SessionBusinessProbe(); var time = new ManualOidcTime();
        using var factory = SessionFactory(authority, probe, time); using var client = Browser(factory);
        var cookie = await LoginSession(client, authority); var (key, ticket) = await Stored(factory, cookie);
        ticket.Properties.UpdateTokenValue("expires_at", time.GetUtcNow().ToString("o"));
        await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
        await Rejected(await Api(client, "/api/admin/session-probe", cookie, "POST"), 401, "reauthentication_required");
        Assert.Equal(1, authority.Redeems); // No automatic challenge or write replay.
        var handshake = await Start(client, "/students?filter=test");
        Assert.Equal("openid profile", handshake.Query["scope"]);
        Assert.False(handshake.Query.ContainsKey("prompt")); Assert.False(handshake.Query.ContainsKey("max_age"));
        if (!reusable)
        {
            // Simulate the browser remaining on the upstream login page before choosing sign-in.
            Assert.Contains("\"requiresReauthentication\":true", await Status(client, cookie));
            Assert.Equal(1, authority.Redeems);
        }
        using var response = await Callback(client, handshake, authority.Code(handshake.Query));
        Assert.Equal("/students?filter=test", response.Headers.Location!.OriginalString);
        Assert.Contains("\"authenticated\":true", await Status(client, SessionCookie(response)));
        Assert.Equal(2, authority.Redeems); Assert.Equal(0, probe.Writes);
        Assert.All(authority.TokenForms, form => Assert.Equal("authorization_code", form["grant_type"]));
    }
}
