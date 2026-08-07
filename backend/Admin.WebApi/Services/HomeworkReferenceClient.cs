using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Admin.WebApi.Services;

public sealed class HomeworkReferenceClient
{
    private readonly HttpClient _client;

    public HomeworkReferenceClient(HttpClient client) => _client = client;

    public async Task<HashSet<string>> GetAllImagePathsAsync(CancellationToken cancellationToken = default)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var page = 1;
        const int pageSize = 200;
        while (true)
        {
            using var response = await _client.GetAsync(
                $"/api/admin/storage/image-references?page={page}&size={pageSize}", cancellationToken);
            response.EnsureSuccessStatusCode();
            var envelope = await response.Content.ReadFromJsonAsync<Envelope<ReferencePage>>(cancellationToken: cancellationToken)
                ?? throw new HttpRequestException("Homework image reference response was empty");
            if (!envelope.Success || envelope.Data == null)
                throw new HttpRequestException(envelope.Message ?? "Homework image reference query failed");
            foreach (var path in envelope.Data.Paths.Where(path => !string.IsNullOrWhiteSpace(path)))
                paths.Add(path);
            if (!envelope.Data.HasMore) break;
            page++;
        }
        return paths;
    }

    private sealed class Envelope<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Message { get; set; }
    }

    private sealed class ReferencePage
    {
        [JsonPropertyName("paths")]
        public List<string> Paths { get; set; } = new();
        [JsonPropertyName("hasMore")]
        public bool HasMore { get; set; }
    }
}
