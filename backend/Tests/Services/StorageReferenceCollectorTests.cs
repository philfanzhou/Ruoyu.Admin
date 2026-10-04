using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Admin.WebApi.Services;
using Microsoft.Extensions.Configuration;
using Ruoyu.Admin.ServiceClients.StorageReferences;
using Xunit;

namespace Admin.WebApi.Tests.Services;

public sealed class StorageReferenceCollectorTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(499)] [InlineData(500)] [InlineData(501)] [InlineData(1001)]
    public async Task Collect_RequiresAllThreeCompleteSnapshotsAndExactTerminalPages(int count)
    {
        using var fixture = new Fixture(count);
        var result = await fixture.Collector.CollectAsync(CancellationToken.None);
        Assert.Equal(count, result.Keys.Count); Assert.Equal(3, result.Snapshots.Count);
        Assert.All(result.Snapshots, snapshot => Assert.False(snapshot.DeletionAuthorized));
        Assert.Equal(3 * (1 + Math.Max(1, (count + 499) / 500)), fixture.Requests);
    }

    public static IEnumerable<object[]> InvalidResponses()
    {
        foreach (var provider in new[] { "student", "mistake", "homework" })
            foreach (var fault in new[] { "401", "403", "404", "410", "503", "redirect", "timeout", "bad-json",
                "version", "coverage", "provider", "id", "count", "page-count", "expiry", "permit", "digest",
                "page-id", "page-index", "page-version", "page-provider", "truncated", "duplicate", "unordered", "last",
                "timezone", "uuid-format", "unknown-field" })
                yield return [provider, fault];
    }

    [Theory]
    [MemberData(nameof(InvalidResponses))]
    public async Task Collect_RejectsAnyIncompleteOrUntrustedProvider(string provider, string fault)
    {
        using var fixture = new Fixture(501, provider, fault);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Collector.CollectAsync(CancellationToken.None));
        Assert.DoesNotContain(fixture.SeenPaths, path => path.Contains("/api/uploads", StringComparison.Ordinal));
    }

    [Fact]
    public void Canonical_DigestMatchesIndependentUnicodeVector()
    {
        var keys = StorageReferenceContract.CanonicalKeys(["uploads/😀.jpg", "uploads/a.jpg", "uploads/A.jpg", "uploads/中.jpg", "uploads/\uE000.jpg"]);
        Assert.Equal("a19c6f14588e50a132f97232022a34a0243eb9e441f96fbe0e26863a4a4628f0", StorageReferenceContract.Digest("student", keys));
    }

    [Fact]
    public async Task DisabledOrCancelledCollectionCannotFallbackAsync()
    {
        using var disabled = new StorageReferenceSigningConfiguration(new ConfigurationBuilder().Build());
        var factory = new Factory(new Handler(_ => throw new InvalidOperationException("No network expected.")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StorageReferenceCollector(factory, disabled).CollectAsync(CancellationToken.None));
        using var fixture = new Fixture(1);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Collector.CollectAsync(cancelled.Token));
        Assert.Equal(0, fixture.Requests);
    }

    [Theory]
    [InlineData("public")] [InlineData("weak")] [InlineData("malformed")] [InlineData("shared-readable")]
    public void SigningConfiguration_RejectsUnusableOrExposedPrivateKeys(string fault)
    {
        var directory = Path.Combine(Path.GetTempPath(), "refs-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var rsa = RSA.Create(fault == "weak" ? 1024 : 2048);
            var path = Path.Combine(directory, "private.pem");
            File.WriteAllText(path, fault switch { "public" => rsa.ExportSubjectPublicKeyInfoPem(), "malformed" => "invalid", _ => rsa.ExportPkcs8PrivateKeyPem() });
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                (fault == "shared-readable" ? UnixFileMode.GroupRead : 0));
            var values = new Dictionary<string,string?> { ["StorageReferences:Enabled"] = "true", ["StorageReferences:KeyId"] = "current", ["StorageReferences:PrivateKeyPath"] = path };
            foreach (var provider in new[] { "student", "mistake", "homework" }) values[$"StorageReferences:Providers:{provider}:BaseUrl"] = $"https://{provider}.fixture/";
            if (OperatingSystem.IsWindows() && fault == "shared-readable") return;
            Assert.Throws<InvalidOperationException>(() => new StorageReferenceSigningConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "refs-collector-" + Guid.NewGuid().ToString("N"));
        private readonly RSA _key = RSA.Create(2048);
        private readonly StorageReferenceSigningConfiguration _signing;
        private readonly Dictionary<string, Guid> _ids = new(StringComparer.Ordinal);
        private readonly string[] _keys;
        private readonly string? _failedProvider;
        private readonly string? _fault;
        public int Requests { get; private set; }
        public List<string> SeenPaths { get; } = [];
        public StorageReferenceCollector Collector { get; }
        private readonly DateTimeOffset _created = DateTimeOffset.UtcNow;
        public Fixture(int count, string? provider = null, string? fault = null)
        {
            Directory.CreateDirectory(_root);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_root, UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            var keyPath = Path.Combine(_root, "private.pem"); File.WriteAllText(keyPath, _key.ExportPkcs8PrivateKeyPem());
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyPath, UnixFileMode.UserRead|UnixFileMode.UserWrite);
            _failedProvider=provider; _fault=fault;
            _keys=Enumerable.Range(0,count).Select(index=>$"uploads/{index:D6}.jpg").ToArray();
            var values = new Dictionary<string,string?> { ["StorageReferences:Enabled"]="true", ["StorageReferences:KeyId"]="fixture-current", ["StorageReferences:PrivateKeyPath"]=keyPath };
            foreach (var name in new[] {"student","mistake","homework"})
            { values[$"StorageReferences:Providers:{name}:BaseUrl"]=$"https://{name}.fixture/"; _ids[name]=Guid.NewGuid(); }
            _signing=new StorageReferenceSigningConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            Collector=new StorageReferenceCollector(new Factory(new Handler(Respond)),_signing);
        }
        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            Requests++; var path=request.RequestUri!.AbsolutePath; SeenPaths.Add(path);
            Assert.StartsWith(StorageReferenceContract.Route,path);
            var token=request.Headers.Authorization?.Parameter ?? throw new InvalidOperationException("Missing service credential.");
            var parts=token.Split('.'); Assert.Equal(3,parts.Length);
            Assert.True(_key.VerifyData(Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]),Decode(parts[2]),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
            using(var payload=JsonDocument.Parse(Decode(parts[1])))
            {
                Assert.Equal("storage.references",payload.RootElement.GetProperty("scope").GetString());
                Assert.Equal(60,payload.RootElement.GetProperty("exp").GetInt64()-payload.RootElement.GetProperty("iat").GetInt64());
            }
            var provider=request.RequestUri.Host.Split('.')[0]; var fault=provider==_failedProvider ? _fault : null;
            if (fault is "401" or "403" or "404" or "410" or "503") return new((HttpStatusCode)int.Parse(fault));
            if (fault=="redirect") return new(HttpStatusCode.Redirect);
            if (fault=="timeout") throw new TaskCanceledException("Injected provider timeout.");
            if (fault=="bad-json") return new(HttpStatusCode.Created) {Content=new StringContent("{",Encoding.UTF8,"application/json")};
            object body; HttpStatusCode status;
            if(request.Method==HttpMethod.Post)
            {
                Assert.Equal(StorageReferenceContract.Route,path);
                var metadata=new StorageReferenceMetadata(StorageReferenceContract.Version,provider,_ids[provider],_created,_created.AddMinutes(10),
                    StorageReferenceContract.Coverage(provider),_keys.Length,500,Math.Max(1,(_keys.Length+499)/500),StorageReferenceContract.Digest(provider,_keys),false);
                metadata=fault switch {
                    "version"=>metadata with {Version="other"}, "coverage"=>metadata with {Coverage="partial"}, "provider"=>metadata with {Provider="other"},
                    "id"=>metadata with {SnapshotId=Guid.Empty},"count"=>metadata with {EntryCount=100001}, "page-count"=>metadata with {PageCount=99},
                    "expiry"=>metadata with {ExpiresAt=_created.AddMinutes(-1)},"permit"=>metadata with {DeletionAuthorized=true},"digest"=>metadata with {Sha256=new string('0',64)},
                    "timezone"=>metadata with {CreatedAt=_created.ToOffset(TimeSpan.FromHours(9)),ExpiresAt=_created.AddMinutes(10).ToOffset(TimeSpan.FromHours(9))},_=>metadata};
                body=metadata; status=HttpStatusCode.Created;
            }
            else
            {
                var index=int.Parse(path.Split('/')[^1]);
                var page=new StorageReferencePage(StorageReferenceContract.Version,provider,_ids[provider],index,_keys.Skip(index*500).Take(500).ToArray(),index==(_keys.Length-1)/500);
                if (_keys.Length==0) page=page with {IsLast=true};
                page=fault switch {"page-id"=>page with {SnapshotId=Guid.NewGuid()},"page-index"=>page with {Index=99},"page-version"=>page with {Version="other"},
                    "page-provider"=>page with {Provider="other"},"truncated"=>page with {Keys=page.Keys.Skip(1).ToArray()},
                    "duplicate"=>page with {Keys=Enumerable.Repeat(page.Keys[0],page.Keys.Count).ToArray()},"unordered"=>page with {Keys=page.Keys.Reverse().ToArray()},
                    "last"=>page with {IsLast=!page.IsLast},_=>page};
                body=page; status=HttpStatusCode.OK;
            }
            var json = JsonSerializer.Serialize(body,new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (fault == "uuid-format") json = json.Replace(_ids[provider].ToString("D"), _ids[provider].ToString("B"), StringComparison.Ordinal);
            if (fault == "unknown-field") json = json[..^1] + ",\"unexpected\":true}";
            return new(status) {Content=new StringContent(json,Encoding.UTF8,"application/json")};
        }
        public void Dispose() { _signing.Dispose(); _key.Dispose(); Directory.Delete(_root,true); }
        private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-','+').Replace('_','/').PadRight((value.Length+3)/4*4,'='));
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) { Assert.Equal(StorageReferenceCollector.ClientName,name); return new(handler,false); } }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); } }
}
