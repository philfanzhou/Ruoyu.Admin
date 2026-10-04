using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ruoyu.Admin.ServiceClients.StorageReferences;

namespace Admin.WebApi.Services;

public sealed class StorageReferenceCollector(IHttpClientFactory clients, StorageReferenceSigningConfiguration signing) : IStorageReferenceCollector
{
    public const string ClientName = "StorageReferences";
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new SnapshotIdConverter());
        return options;
    }

    public async Task<StorageReferenceCollection> CollectAsync(CancellationToken cancellationToken)
    {
        if (!signing.Enabled || signing.Key is null || signing.Providers.Count != 3) throw Unavailable();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromMinutes(2));
        var token = budget.Token;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var snapshots = new List<StorageReferenceMetadata>();
        using var client = clients.CreateClient(ClientName);
        foreach (var provider in new[] { "student", "mistake", "homework" })
        {
            var root = signing.Providers[provider];
            var metadata = await SendAsync<StorageReferenceMetadata>(client, root, HttpMethod.Post,
                StorageReferenceContract.Route, HttpStatusCode.Created, token);
            var now = DateTimeOffset.UtcNow;
            if (metadata.Version != StorageReferenceContract.Version || metadata.Provider != provider ||
                metadata.SnapshotId == Guid.Empty || metadata.Coverage != StorageReferenceContract.Coverage(provider) ||
                metadata.DeletionAuthorized || metadata.CreatedAt.Offset != TimeSpan.Zero || metadata.ExpiresAt.Offset != TimeSpan.Zero ||
                metadata.CreatedAt > now.AddSeconds(5) || metadata.ExpiresAt <= now ||
                metadata.ExpiresAt != metadata.CreatedAt.AddMinutes(10) || metadata.PageSize != StorageReferenceContract.PageSize ||
                metadata.EntryCount < 0 || metadata.EntryCount > StorageReferenceContract.MaximumEntries ||
                metadata.PageCount != Math.Max(1, (metadata.EntryCount + metadata.PageSize - 1) / metadata.PageSize)) throw Unavailable();
            var captured = new List<string>();
            long bytes = 0;
            for (var index = 0; index < metadata.PageCount; index++)
            {
                var page = await SendAsync<StorageReferencePage>(client, root, HttpMethod.Get,
                    $"{StorageReferenceContract.Route}/{metadata.SnapshotId:D}/pages/{index}", HttpStatusCode.OK, token);
                var expectedCount = Math.Min(metadata.PageSize, metadata.EntryCount - index * metadata.PageSize);
                if (page.Version != metadata.Version || page.Provider != provider || page.SnapshotId != metadata.SnapshotId ||
                    page.Index != index || page.Keys is null || page.Keys.Count != expectedCount ||
                    page.IsLast != (index == metadata.PageCount - 1)) throw Unavailable();
                foreach (var key in page.Keys)
                {
                    StorageReferenceContract.ValidateKey(key);
                    bytes += 4L + StorageReferenceContract.Utf8.GetByteCount(key);
                    if (bytes > StorageReferenceContract.MaximumBytes ||
                        captured.Count != 0 && StorageReferenceContract.CompareKeys(captured[^1], key) >= 0) throw Unavailable();
                    captured.Add(key);
                }
            }
            if (metadata.ExpiresAt <= DateTimeOffset.UtcNow || captured.Count != metadata.EntryCount ||
                StorageReferenceContract.Digest(provider, captured) != metadata.Sha256) throw Unavailable();
            snapshots.Add(metadata); keys.UnionWith(captured);
        }
        if (snapshots.Any(snapshot => snapshot.ExpiresAt <= DateTimeOffset.UtcNow)) throw Unavailable();
        return new(keys, snapshots);
    }

    private async Task<T> SendAsync<T>(HttpClient client, Uri root, HttpMethod method, string path,
        HttpStatusCode expected, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, new Uri(root, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode != expected || response.Content.Headers.ContentType?.MediaType != "application/json") throw Unavailable();
        // Bound the decoded wire payload even when the server omits Content-Length.
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var bytes = new MemoryStream();
        var buffer = new byte[81920]; int length;
        while ((length = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (bytes.Length + length > StorageReferenceContract.MaximumBytes * 6L) throw Unavailable();
            await bytes.WriteAsync(buffer.AsMemory(0, length), token);
        }
        return JsonSerializer.Deserialize<T>(bytes.ToArray(), Json) ?? throw Unavailable();
    }

    private string CreateToken()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = Base64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT", kid = signing.KeyId }));
        var payload = Base64(JsonSerializer.SerializeToUtf8Bytes(new { iss = signing.Issuer, aud = signing.Audience,
            sub = signing.Subject, iat = now, nbf = now, exp = now + 60, scope = "storage.references" }));
        var content = header + "." + payload;
        byte[] signature;
        lock (signing.Key!) signature = signing.Key!.SignData(Encoding.ASCII.GetBytes(content), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return content + "." + Base64(signature);
    }
    private static string Base64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static InvalidOperationException Unavailable() => new("Storage reference collection unavailable or incomplete.");

    private sealed class SnapshotIdConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString();
            if (!Guid.TryParseExact(text, "D", out var value) || text != value.ToString("D") || value == Guid.Empty)
                throw new JsonException("Invalid snapshot identity.");
            return value;
        }
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("D"));
    }
}
