using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.ServiceClients;

public sealed class MistakeSessionClientTests
{
    public static IEnumerable<object[]> Methods => Enumerable.Range(0, 14).Select(i => new object[] { i });
    internal static async Task<object?> Call(IMistakeHttpClient client, int method, CancellationToken ct = default) => method switch
    {
        0 => await client.GetMistakeItemListAsync("student & ? #", 1, 2, MistakeReviewStatus.PendingReview, 1, 10, ct),
        1 => await client.GetMistakeItemAsync("item space?#", ct),
        2 => await client.GetMistakeItemsByUploadAsync("upload space?#", ct),
        3 => await client.GetPendingReviewUploadsAsync(1, 10, [1, 2], ct),
        4 => await client.ReviewMistakeItemAsync("item space?#", MistakeReviewStatus.Confirmed, "reviewer", null, 2, 1, null, ct),
        5 => await client.AddMistakeItemAsync("student", 1, 2, "upload", null, [], ct),
        6 => await client.DeleteMistakeItemAsync("item space?#", ct),
        7 => await client.ReanalyzeMistakeItemAsync("item space?#", "reviewer", "student", null, ct),
        8 => await client.GetReanalyzeJobsAsync("reviewer", ["job"], ct),
        9 => await client.GetActiveReanalyzeJobsAsync("reviewer ?&#", ct),
        10 => await client.UpdateMistakeItemAsync("item space?#", "student", 1, 2, null, ct),
        11 => await client.SubmitMistakeUploadAsync("student", 1, 2, ["uploads/a.png"], null, "upload", ct),
        12 => await client.CompleteUploadReviewAsync("upload", "admin-legacy-cleanup", ct),
        13 => await client.GetPresignedUrlAsync("mistakes/path ?&#.png", 3600, null, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    internal static object Data(int method) => method switch
    {
        0 => new { items = Array.Empty<object>(), pageMeta = new { page = 1, size = 10, totalCount = 0, totalPages = 0 } },
        1 or 5 or 10 => new { id = "item", studentId = "student", sourceRegions = Array.Empty<object>() },
        2 => new { items = Array.Empty<object>() },
        3 => new { uploads = Array.Empty<object>(), pageMeta = new { page = 1, size = 10 } },
        4 or 12 => new { success = true, removedImagePaths = Array.Empty<string>() },
        6 => new { },
        7 => new { success = true, jobId = "job" },
        8 or 9 => new { success = true, jobs = Array.Empty<object>() },
        11 => new { success = true, createdItemIds = new[] { "item" } },
        13 => new { url = "https://s3.example.test/bucket/key?signature=valid", expirySeconds = 3600 },
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    [Theory, MemberData(nameof(Methods))]
    public async Task All14_RealHttpSuccessAndFailureMatrixNeverDegradesOrReplays(int method)
    {
        await using var receiver = await Receiver.StartAsync();
        using var http = new HttpClient { BaseAddress = receiver.Origin, Timeout = TimeSpan.FromSeconds(2) };
        var client = new MistakeHttpClient(http, NullLogger<MistakeHttpClient>.Instance, new(true));
        receiver.Body = System.Text.Json.JsonSerializer.Serialize(new { success = true, data = Data(method) });
        Assert.NotNull(await Call(client, method));
        Assert.Equal(1, receiver.Sends);
        Assert.StartsWith("/api/mistakes", receiver.LastPath);
        foreach (var status in new[] { 401, 403, 302, 307, 500, 503 })
        {
            receiver.Status = status; receiver.Body = "upstream-token-canary signed-url-canary";
            var before = receiver.Sends;
            var error = await Assert.ThrowsAsync<MistakeDownstreamException>(() => Call(client, method));
            Assert.Equal(before + 1, receiver.Sends); Assert.Null(error.InnerException);
            Assert.DoesNotContain("canary", error.ToString());
        }
        foreach (var status in new[] { 400, 409 })
        {
            receiver.Status = status; receiver.Body = "{\"success\":false,\"message\":\"secret-canary\"}";
            var error = await Assert.ThrowsAnyAsync<MistakeBoundaryException>(() => Call(client, method));
            Assert.Equal(status == 400 ? typeof(MistakeBadRequestException) : typeof(MistakeConflictException), error.GetType());
            Assert.DoesNotContain("canary", error.ToString());
            receiver.Body = "not-json";
            await Assert.ThrowsAsync<MistakeDownstreamException>(() => Call(client, method));
        }
        receiver.Status = 200;
        foreach (var body in new[] { "", "not-json", "null", "[]", "{}", "{\"success\":true,\"data\":null}", "{\"success\":true,\"data\":[]}" })
        {
            // DELETE's published success envelope intentionally has no required data.
            if (method == 6 && body.StartsWith("{\"success\":true")) continue;
            receiver.Body = body;
            await Assert.ThrowsAsync<MistakeDownstreamException>(() => Call(client, method));
        }
        receiver.Body = "{\"success\":false,\"message\":\"secret-canary\"}";
        await Assert.ThrowsAsync<MistakeBadRequestException>(() => Call(client, method));
        if (method != 6)
        {
            receiver.Body = "{\"success\":true,\"data\":{}}";
            await Assert.ThrowsAsync<MistakeDownstreamException>(() => Call(client, method));
            receiver.Body = "{\"success\":true,\"data\":{\"success\":false,\"errorMessage\":\"secret-canary\"}}";
            await Assert.ThrowsAsync<MistakeBadRequestException>(() => Call(client, method));
        }
        receiver.Status = 404; receiver.Body = "not-json";
        if (method == 1) Assert.Null(await Call(client, method));
        else await Assert.ThrowsAsync<MistakeDownstreamException>(() => Call(client, method));
        receiver.Status = 200; receiver.Delay = true;
        using var timeoutHttp = new HttpClient { BaseAddress = receiver.Origin, Timeout = TimeSpan.FromMilliseconds(70) };
        var timeoutClient = new MistakeHttpClient(timeoutHttp, NullLogger<MistakeHttpClient>.Instance, new(true));
        await Assert.ThrowsAsync<MistakeDownstreamException>(() => Call(timeoutClient, method));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var sends = receiver.Sends;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Call(client, method, cancelled.Token));
        Assert.Equal(sends, receiver.Sends);
        using var during = new CancellationTokenSource(TimeSpan.FromMilliseconds(70));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Call(client, method, during.Token));
    }

    [Theory, MemberData(nameof(Methods))]
    public async Task All14_RealConnectionRefusedAndTlsErrorsAreSafe(int method)
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(1) };
        var client = new MistakeHttpClient(http, NullLogger<MistakeHttpClient>.Instance, new(true));
        await Assert.ThrowsAsync<MistakeDownstreamException>(() => Call(client, method));
        await using var receiver = await Receiver.StartAsync();
        using var tls = new HttpClient { BaseAddress = new Uri(receiver.Origin.AbsoluteUri.Replace("http:", "https:")), Timeout = TimeSpan.FromSeconds(1) };
        await Assert.ThrowsAsync<MistakeDownstreamException>(() => Call(new MistakeHttpClient(tls, NullLogger<MistakeHttpClient>.Instance, new(true)), method));
    }

    internal sealed class Receiver(WebApplication app) : IAsyncDisposable
    {
        internal Uri Origin = null!;
        internal int Status = 200, Sends;
        internal string Body = "", LastPath = "";
        internal string? Location, LastAuthorization;
        internal bool LastCookie, LastCsrf, LastTrace;
        internal bool Delay;
        internal static async Task<Receiver> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            var app = builder.Build(); var receiver = new Receiver(app);
            app.Run(async context =>
            {
                Interlocked.Increment(ref receiver.Sends); receiver.LastPath = context.Request.Path;
                receiver.LastAuthorization = context.Request.Headers.Authorization.ToString();
                receiver.LastCookie = context.Request.Headers.ContainsKey("Cookie");
                receiver.LastCsrf = context.Request.Headers.ContainsKey("X-CSRF-TOKEN");
                receiver.LastTrace = context.Request.Headers.ContainsKey("traceparent");
                if (receiver.Delay) { try { await Task.Delay(1000, context.RequestAborted); } catch (OperationCanceledException) { return; } }
                context.Response.StatusCode = receiver.Status; context.Response.ContentType = "application/json";
                if (receiver.Location is not null) context.Response.Headers.Location = receiver.Location;
                context.Response.Headers.SetCookie = "downstream-private=do-not-replay; Path=/";
                await context.Response.WriteAsync(receiver.Body);
            });
            await app.StartAsync();
            receiver.Origin = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            return receiver;
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
