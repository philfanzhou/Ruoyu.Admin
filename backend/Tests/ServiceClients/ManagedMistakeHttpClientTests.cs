using System.Net;
using System.Text;
using System.Text.Json;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.ServiceClients;

public sealed class ManagedMistakeHttpClientTests
{
    private static readonly ManagedMistakeUpload Upload = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), 2, 7, ["uploads/题目/A.PNG", "uploads/题目/a.PNG"], "fixed cause");
    private static readonly Guid Item = Guid.NewGuid();
    private const string Caller = "unit-test-authenticated-caller";

    private sealed class Wire(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); return send(request, cancellationToken); }
    }

    private static ManagedMistakeHttpClient Client(HttpMessageHandler wire) => new(new HttpClient(wire)
        { BaseAddress = new Uri("https://mistake.example.test") });
    private static HttpResponseMessage Reply(int status, string body) => new((HttpStatusCode)status)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task CompletedRequiresRealEnvelopeAndIds_TransmitsExactFixedProposalOnlyOnUpload()
    {
        var wire = new Wire(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://mistake.example.test/api/mistakes/upload", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal(Caller, request.Headers.Authorization.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(Upload.RequestKey, body.RootElement.GetProperty("requestKey").GetGuid());
            Assert.Equal(Upload.ExpectedContentRevision, body.RootElement.GetProperty("expectedContentRevision").GetGuid());
            Assert.Equal(Upload.ImagePaths, body.RootElement.GetProperty("imagePaths").EnumerateArray().Select(p => p.GetString()));
            Assert.False(body.RootElement.TryGetProperty("actor", out _)); Assert.False(body.RootElement.TryGetProperty("role", out _));
            return Reply(200, JsonSerializer.Serialize(new { success = true, data = new { success = true, createdItemIds = new[] { Item } } }));
        });
        var result = await Client(wire).SubmitAsync(Upload, Caller, default);
        Assert.Equal("Completed", result.State); Assert.Equal([Item], result.CreatedItemIds); Assert.Equal(200, result.HttpStatus); Assert.Equal(1, wire.Calls);
    }

    public static IEnumerable<object[]> InvalidCompletion()
    {
        foreach (var body in new[] { "null", "[]", "not-json", "{}", "{\"success\":true}",
            "{\"success\":true,\"data\":{\"success\":true,\"createdItemIds\":[]}}",
            "{\"success\":true,\"data\":{\"success\":true,\"createdItemIds\":[\"bad-id\"]}}",
            "{\"success\":true,\"data\":{\"success\":true,\"createdItemIds\":[\"00000000-0000-0000-0000-000000000000\"]}}",
            "{\"success\":false,\"success\":true,\"data\":{\"success\":true,\"createdItemIds\":[\""+Item+"\"]}}",
            JsonSerializer.Serialize(new { success = true, data = new { success = true, createdItemIds = new[] { Item, Item } } }),
            "{\"success\":true,\"data\":{\"success\":\"true\",\"createdItemIds\":[\""+Item+"\"]}}" }) yield return [body];
    }

    [Theory]
    [MemberData(nameof(InvalidCompletion))]
    public async Task MalformedOrMissingIdsIsUnknown_NeverEmptySuccess(string body)
    {
        var wire = new Wire((_, _) => Task.FromResult(Reply(200, body)));
        var result = await Client(wire).SubmitAsync(Upload, Caller, default);
        Assert.Equal("Unknown", result.State); Assert.Empty(result.CreatedItemIds); Assert.Equal(1, wire.Calls);
    }

    [Theory]
    [InlineData(409, "{\"success\":false,\"errorKind\":\"request_payload_conflict\"}", "Failed", "request_payload_conflict")]
    [InlineData(403, "{\"success\":false,\"errorKind\":\"student_conflict\"}", "Failed", "student_conflict")]
    [InlineData(400, "{\"success\":false,\"message\":\"do-not-return-sensitive-provider-text\"}", "Failed", "provider_rejected")]
    [InlineData(500, "{\"success\":true,\"data\":{\"success\":true}}", "Unknown", "provider_unavailable")]
    [InlineData(503, "not-json", "Unknown", "provider_unavailable")]
    [InlineData(302, "", "Unknown", "provider_unavailable")]
    [InlineData(202, "{\"success\":true,\"data\":{\"success\":false}}", "Unknown", "invalid_response")]
    [InlineData(200, "{\"success\":true,\"data\":{\"success\":false}}", "Failed", "provider_rejected")]
    [InlineData(200, "{\"success\":false}", "Failed", "provider_rejected")]
    public async Task ActualStatusAndEnvelopeDetermineFailureWithoutRelayingRawBodies(int status, string body, string state, string error)
    {
        var wire = new Wire((_, _) => Task.FromResult(Reply(status, body)));
        var result = await Client(wire).SubmitAsync(Upload, Caller, default);
        Assert.Equal(state, result.State); Assert.Equal(error, result.ErrorKind); Assert.Empty(result.CreatedItemIds); Assert.Equal(1, wire.Calls);
    }

    [Fact]
    public async Task OversizedOrTruncatedResponseRemainsUnknown()
    {
        var wire = new Wire((_, _) => Task.FromResult(Reply(200, new string('x', 65537))));
        var result = await Client(wire).SubmitAsync(Upload, Caller, default);
        Assert.Equal("Unknown", result.State); Assert.Equal("invalid_response", result.ErrorKind);
    }

    [Fact]
    public async Task DisconnectDoesNotRetryOrChangeRequestKey()
    {
        var wire = new Wire((_, _) => throw new HttpRequestException("unit connection lost after send"));
        var result = await Client(wire).SubmitAsync(Upload, Caller, default);
        Assert.Equal("Unknown", result.State); Assert.Equal(1, wire.Calls);
        Assert.NotEqual(Guid.Empty, Upload.RequestKey);
    }

    [Fact]
    public async Task CancellationBeforeSendIsUnknownAndHasNoIo()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var wire = new Wire((_, _) => throw new InvalidOperationException("No IO expected"));
        var result = await Client(wire).SubmitAsync(Upload, Caller, cancellation.Token);
        Assert.Equal("Unknown", result.State); Assert.Equal("cancelled", result.ErrorKind); Assert.Equal(0, wire.Calls);
    }

    [Fact]
    public async Task CancellationDuringSendFlowsToTransportAndDoesNotRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var wire = new Wire(async (_, ct) => { cancellation.Cancel(); await Task.Delay(100, ct); return Reply(200, "{}"); });
        var result = await Client(wire).SubmitAsync(Upload, Caller, cancellation.Token);
        Assert.Equal("Unknown", result.State); Assert.Equal("cancelled", result.ErrorKind); Assert.Equal(1, wire.Calls);
    }

    [Fact]
    public async Task NonHttpsOrMissingCallerNeverSends()
    {
        var wire = new Wire((_, _) => throw new InvalidOperationException("No IO expected"));
        var client = new ManagedMistakeHttpClient(new HttpClient(wire) { BaseAddress = new Uri("http://localhost:5007") });
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitAsync(Upload, Caller, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(wire).SubmitAsync(Upload, "", default));
        Assert.Equal(0, wire.Calls);
    }
}
