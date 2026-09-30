using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Admin.WebApi.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Moq;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Integration;

public sealed partial class AdminOidcTests
{
    [Theory]
    [InlineData(true, false)][InlineData(true, true)][InlineData(false, false)]
    public async Task AssociationsSession_TokenFollowsApiSwitchAndKeepsPartialAggregation(bool api, bool portals)
    {
        using var authority = new OidcTestAuthority();
        var student = new Mock<IStudentHttpClient>(); var accounts = new List<string> { "known" }; var failStudent = false;
        student.Setup(s => s.GetIdentityAccountsByStudentIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => failStudent ? throw new HttpRequestException("fake.unreachable") : accounts);
        var admins = new MutableAdmins();
        var teacher = new PortalSessionCapture { ResponseBody = "{\"data\":[{\"userId\":\"KNOWN\",\"username\":\"teacher\",\"subjects\":[1,\"math\"]},{\"userId\":\"other\"}]}" };
        var assistant = new PortalSessionCapture { ResponseBody = "[{\"UserId\":\"known\",\"Phone\":\"fake-phone\"}]" };
        using var factory = OidcFactory(authority, configure: services =>
        {
            services.Replace(ServiceDescriptor.Singleton(student.Object));
            services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<AdminPortalOptions>>(admins));
            services.AddHttpClient("TeacherPortal").ConfigurePrimaryHttpMessageHandler(() => teacher);
            services.AddHttpClient("AssistantPortal").ConfigurePrimaryHttpMessageHandler(() => assistant);
            services.Configure<JwtBearerOptions>("Bearer", options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = OidcTestAuthority.Issuer };
                metadata.SigningKeys.Add(authority.SigningKey); options.Configuration = metadata;
            });
        }, sessionApi: api, portalProxies: portals);
        using var client = Browser(factory);
        var cookie = api ? await LoginSession(client, authority) : null;
        var authorization = api ? null : new[] { "Bearer " + authority.LegacyBearer() };
        var path = $"/api/admin/students/{Guid.NewGuid()}/linked-accounts";
        async Task<JsonElement> Query()
        { using var response = await Api(client, path, cookie, authorization: authorization); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
        var result = await Query();
        Assert.Single(result.GetProperty("teachers").EnumerateArray()); Assert.Single(result.GetProperty("assistants").EnumerateArray());
        Assert.Equal("1,math", result.GetProperty("teachers")[0].GetProperty("subjects").GetString());
        foreach (var capture in new[] { teacher, assistant })
        {
            Assert.Equal(api ? "Bearer " + OidcTestAuthority.AccessToken : authorization![0], capture.Requests.Single().Headers["Authorization"]);
            Assert.False(capture.Requests.Single().Headers.ContainsKey("Cookie"));
        }
        teacher.Status = HttpStatusCode.Forbidden; result = await Query();
        Assert.Empty(result.GetProperty("teachers").EnumerateArray()); Assert.Single(result.GetProperty("assistants").EnumerateArray());
        teacher.Status = HttpStatusCode.OK; teacher.ResponseBody = "invalid-json"; result = await Query();
        Assert.Empty(result.GetProperty("teachers").EnumerateArray()); Assert.Single(result.GetProperty("assistants").EnumerateArray());
        teacher.Fail = true; result = await Query();
        Assert.Empty(result.GetProperty("teachers").EnumerateArray()); Assert.Single(result.GetProperty("assistants").EnumerateArray());
        var prior = assistant.Requests.Count; accounts.Clear(); result = await Query();
        Assert.Empty(result.GetProperty("assistants").EnumerateArray()); Assert.Equal(prior, assistant.Requests.Count);
        failStudent = true; result = await Query(); Assert.Empty(result.GetProperty("teachers").EnumerateArray());
        if (api)
        {
            student.Invocations.Clear(); teacher.Requests.Clear(); assistant.Requests.Clear();
            await Rejected(await Api(client, path), 401, "unauthorized");
            await Rejected(await Api(client, path, cookie, authorization: ["Bearer wrong"]), 401, "unauthorized");
            admins.CurrentValue.AdminUserIds.Clear();
            await Rejected(await Api(client, path, cookie), 403, "forbidden");
            admins.CurrentValue.AdminUserIds.Add("fake-subject");
            var (key, ticket) = await Stored(factory, cookie!);
            ticket.Properties.UpdateTokenValue("expires_at", DateTimeOffset.UtcNow.AddHours(-1).ToString("o"));
            await factory.Services.GetRequiredService<MemoryTicketStore>().RenewAsync(key, ticket);
            await Rejected(await Api(client, path, cookie), 401, "unauthorized");
            Assert.Empty(student.Invocations); Assert.Empty(teacher.Requests); Assert.Empty(assistant.Requests);
        }
    }
    [Fact]
    public async Task AssociationsSession_CancellationStopsStudentAndPortalWithoutEmptySuccess()
    {
        using var authority = new OidcTestAuthority();
        var student = new Mock<IStudentHttpClient>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        student.Setup(s => s.GetIdentityAccountsByStudentIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string id, CancellationToken ct) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
                return new List<string>();
            });
        var portal = new PortalSessionCapture();
        using var factory = OidcFactory(authority, configure: services =>
        {
            services.Replace(ServiceDescriptor.Singleton(student.Object));
            services.AddHttpClient("TeacherPortal").ConfigurePrimaryHttpMessageHandler(() => portal);
            services.AddHttpClient("AssistantPortal").ConfigurePrimaryHttpMessageHandler(() => portal);
        }, sessionApi: true);
        using var client = Browser(factory); var cookie = await LoginSession(client, authority);
        var path = $"/api/admin/students/{Guid.NewGuid()}/linked-accounts";
        using var pre = new CancellationTokenSource(); pre.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Api(client, path, cookie, cancellation: pre.Token));
        Assert.Empty(student.Invocations);
        using var ongoing = new CancellationTokenSource(); var pending = Api(client, path, cookie, cancellation: ongoing.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); ongoing.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Empty(portal.Requests);
    }
}
