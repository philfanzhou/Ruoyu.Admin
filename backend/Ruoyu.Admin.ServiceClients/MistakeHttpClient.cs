using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ruoyu.Admin.ServiceClients;

// ===== Mistake service DTOs =====
// These DTOs mirror the HTTP response shapes published by the Mistake service, which lives in
// the separate, non-public Ruoyu.Study repository. They are
// hand-maintained copies: this repository has no compile-time reference to the Mistake service,
// so an upstream contract change surfaces as a deserialization mismatch, not a build error.
// Field names and JSON shapes must match the Mistake service HTTP endpoints exactly.

public enum MistakeReviewStatus
{
    Unspecified = 0,
    PendingReview = 1,
    Confirmed = 2,
    Rejected = 3,
}

public class MistakeBoundingBoxDto
{
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
}

public class MistakeSourceRegionDto
{
    public string SourceImagePath { get; set; } = string.Empty;
    public MistakeBoundingBoxDto? BoundingBox { get; set; }
}

public class MistakeItemDto
{
    public string Id { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public int Subject { get; set; }
    public int Grade { get; set; }
    public string SourceUploadId { get; set; } = string.Empty;
    public List<MistakeSourceRegionDto> SourceRegions { get; set; } = new();
    public MistakeReviewStatus ReviewStatus { get; set; }
    public string ReviewerId { get; set; } = string.Empty;
    public string ReviewedAt { get; set; } = string.Empty;
    public string RootCause { get; set; } = string.Empty;
    public string QuestionId { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

public class MistakePageMetaDto
{
    public int Page { get; set; }
    public int Size { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public class MistakeItemPageResult
{
    public List<MistakeItemDto> Items { get; set; } = new();
    public MistakePageMetaDto PageMeta { get; set; } = new();
}

public class MistakeItemsByUploadResult
{
    public List<MistakeItemDto> Items { get; set; } = new();
}

public class UpdateMistakeItemResult
{
    public string Id { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public int Subject { get; set; }
    public int Grade { get; set; }
    public string SourceUploadId { get; set; } = string.Empty;
    public MistakeReviewStatus ReviewStatus { get; set; }
}

public class SubmitMistakeUploadResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public List<string> CreatedItemIds { get; set; } = new();
}

public class CompleteUploadReviewResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public List<string> RemovedImagePaths { get; set; } = new();
}

public class MistakePresignedUrlResult
{
    public string Url { get; set; } = string.Empty;
    public int ExpirySeconds { get; set; }
}

// ===== Interface =====

public interface IMistakeHttpClient
{
    Task<MistakeItemPageResult> GetMistakeItemListAsync(string studentId, int subject, int grade, MistakeReviewStatus reviewStatus, int page, int size, CancellationToken ct = default);
    Task<MistakeItemDto?> GetMistakeItemAsync(string id, CancellationToken ct = default);
    Task<MistakeItemsByUploadResult> GetMistakeItemsByUploadAsync(string sourceUploadId, CancellationToken ct = default);
    Task<UpdateMistakeItemResult> UpdateMistakeItemAsync(string id, string studentId, int subject, int grade, IReadOnlyList<MistakeSourceRegionDto>? sourceRegions, CancellationToken ct = default);
    Task<SubmitMistakeUploadResult> SubmitMistakeUploadAsync(string studentId, int subject, int grade, IReadOnlyList<string> imagePaths, string? rootCause, string sourceUploadId, CancellationToken ct = default);
    Task<CompleteUploadReviewResult> CompleteUploadReviewAsync(string sourceUploadId, string reviewerId, CancellationToken ct = default);
    Task<MistakePresignedUrlResult> GetPresignedUrlAsync(string objectPath, int expirySeconds, string? size, CancellationToken ct = default);
}

// ===== Implementation =====

public class MistakeHttpClient : IMistakeHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MistakeHttpClient> _logger;
    private readonly bool _useSessionToken;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MistakeHttpClient(HttpClient httpClient, ILogger<MistakeHttpClient> logger, MistakeClientPolicy? policy = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _useSessionToken = policy?.UseSessionToken == true;
    }

    // 1. GET /api/mistakes
    // Unlike the methods below, this one propagates failures (transport errors, non-success
    // envelopes) instead of degrading to an empty page: it feeds the OSS audit reference
    // aggregation (OssAuditWorker) and the pre-delete recheck (OssAuditController), where an
    // empty result would be indistinguishable from "no mistake references" and could let a
    // Mistake-service outage downgrade an audit or authorize real object deletions. Callers
    // must catch; all three current callers do.
    public async Task<MistakeItemPageResult> GetMistakeItemListAsync(string studentId, int subject, int grade, MistakeReviewStatus reviewStatus, int page, int size, CancellationToken ct = default)
    {
        if (_useSessionToken)
        {
            return await SendStrictAsync<MistakeItemPageResult>(HttpMethod.Get, $"/api/mistakes?page={page}&size={size}&subject={subject}&grade={grade}&reviewStatus={(int)reviewStatus}&studentId={Uri.EscapeDataString(studentId)}", null, ct);
        }

        var query = $"?page={page}&size={size}&subject={subject}&grade={grade}&reviewStatus={(int)reviewStatus}";
        if (!string.IsNullOrWhiteSpace(studentId))
            query += $"&studentId={Uri.EscapeDataString(studentId)}";

        using var response = await _httpClient.GetAsync($"/api/mistakes{query}", ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<MistakeItemPageResult>>(JsonOptions, ct);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            var msg = envelope?.Message ?? "GetMistakeItemList failed";
            _logger.LogWarning("GetMistakeItemList failed: {Message}, Status: {Status}", msg, response.StatusCode);
            throw new HttpRequestException(msg, null, response.StatusCode);
        }

        return envelope.Data ?? new MistakeItemPageResult();
    }

    // 2. GET /api/mistakes/{id}
    public async Task<MistakeItemDto?> GetMistakeItemAsync(string id, CancellationToken ct = default)
    {
        if (_useSessionToken)
        {
            return await SendStrictAsync<MistakeItemDto>(HttpMethod.Get, $"/api/mistakes/{Uri.EscapeDataString(id)}", null, ct, allowNotFound: true);
        }

        try
        {
            using var response = await _httpClient.GetAsync($"/api/mistakes/{id}", ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<MistakeItemDto>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetMistakeItem failed";
                _logger.LogWarning("GetMistakeItem failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return null;
            }

            return envelope.Data;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetMistakeItem HTTP request failed");
            return null;
        }
    }

    // 3. GET /api/mistakes/by-upload/{sourceUploadId}
    public async Task<MistakeItemsByUploadResult> GetMistakeItemsByUploadAsync(string sourceUploadId, CancellationToken ct = default)
    {
        if (_useSessionToken)
        {
            return await SendStrictAsync<MistakeItemsByUploadResult>(HttpMethod.Get, $"/api/mistakes/by-upload/{Uri.EscapeDataString(sourceUploadId)}", null, ct);
        }

        try
        {
            using var response = await _httpClient.GetAsync($"/api/mistakes/by-upload/{Uri.EscapeDataString(sourceUploadId)}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<MistakeItemsByUploadResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetMistakeItemsByUpload failed";
                _logger.LogWarning("GetMistakeItemsByUpload failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new MistakeItemsByUploadResult();
            }

            return envelope.Data ?? new MistakeItemsByUploadResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetMistakeItemsByUpload HTTP request failed");
            return new MistakeItemsByUploadResult();
        }
    }

    // 4. PUT /api/mistakes/{id}
    public async Task<UpdateMistakeItemResult> UpdateMistakeItemAsync(string id, string studentId, int subject, int grade, IReadOnlyList<MistakeSourceRegionDto>? sourceRegions, CancellationToken ct = default)
    {
        if (_useSessionToken)
        {
            var item = await SendStrictAsync<MistakeItemDto>(HttpMethod.Put, $"/api/mistakes/{Uri.EscapeDataString(id)}", new { id, studentId, subject, grade, sourceRegions }, ct);
            return new UpdateMistakeItemResult { Id = item.Id, StudentId = item.StudentId, Subject = item.Subject, Grade = item.Grade, SourceUploadId = item.SourceUploadId, ReviewStatus = item.ReviewStatus };
        }

        try
        {
            var body = new
            {
                id,
                studentId,
                subject,
                grade,
                sourceRegions
            };
            using var response = await _httpClient.PutAsJsonAsync($"/api/mistakes/{id}", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<MistakeItemDto>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "UpdateMistakeItem failed";
                _logger.LogWarning("UpdateMistakeItem failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new UpdateMistakeItemResult();
            }

            var item = envelope.Data;
            return new UpdateMistakeItemResult
            {
                Id = item?.Id ?? string.Empty,
                StudentId = item?.StudentId ?? string.Empty,
                Subject = item?.Subject ?? 0,
                Grade = item?.Grade ?? 0,
                SourceUploadId = item?.SourceUploadId ?? string.Empty,
                ReviewStatus = item?.ReviewStatus ?? MistakeReviewStatus.Unspecified
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "UpdateMistakeItem HTTP request failed");
            return new UpdateMistakeItemResult();
        }
    }

    // 5. POST /api/mistakes/upload
    public async Task<SubmitMistakeUploadResult> SubmitMistakeUploadAsync(string studentId, int subject, int grade, IReadOnlyList<string> imagePaths, string? rootCause, string sourceUploadId, CancellationToken ct = default)
    {
        if (_useSessionToken)
        {
            return await SendStrictAsync<SubmitMistakeUploadResult>(HttpMethod.Post, "/api/mistakes/upload", new { studentId, subject, grade, imagePaths, rootCause, sourceUploadId }, ct);
        }

        try
        {
            var body = new
            {
                studentId,
                subject,
                grade,
                imagePaths,
                rootCause,
                sourceUploadId
            };
            using var response = await _httpClient.PostAsJsonAsync("/api/mistakes/upload", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SubmitMistakeUploadResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "SubmitMistakeUpload failed";
                _logger.LogWarning("SubmitMistakeUpload failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new SubmitMistakeUploadResult { Success = false, ErrorMessage = msg };
            }

            return envelope.Data ?? new SubmitMistakeUploadResult { Success = false, ErrorMessage = "Empty response data" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "SubmitMistakeUpload HTTP request failed");
            return new SubmitMistakeUploadResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // 6. POST /api/mistakes/complete-review
    public async Task<CompleteUploadReviewResult> CompleteUploadReviewAsync(string sourceUploadId, string reviewerId, CancellationToken ct = default)
    {
        if (_useSessionToken)
        {
            return await SendStrictAsync<CompleteUploadReviewResult>(HttpMethod.Post, "/api/mistakes/complete-review", new { sourceUploadId, reviewerId }, ct);
        }

        try
        {
            var body = new { sourceUploadId, reviewerId };
            using var response = await _httpClient.PostAsJsonAsync("/api/mistakes/complete-review", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CompleteUploadReviewResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "CompleteUploadReview failed";
                _logger.LogWarning("CompleteUploadReview failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new CompleteUploadReviewResult { Success = false, ErrorMessage = msg };
            }

            return envelope.Data ?? new CompleteUploadReviewResult { Success = false, ErrorMessage = "Empty response data" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "CompleteUploadReview HTTP request failed");
            return new CompleteUploadReviewResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // 7. POST /api/mistakes/presigned-url
    public async Task<MistakePresignedUrlResult> GetPresignedUrlAsync(string objectPath, int expirySeconds, string? size, CancellationToken ct = default)
    {
        if (_useSessionToken)
        {
            return await SendStrictAsync<MistakePresignedUrlResult>(HttpMethod.Post, "/api/mistakes/presigned-url", new { objectPath, expirySeconds, size }, ct);
        }

        try
        {
            var body = new { objectPath, expirySeconds, size };
            using var response = await _httpClient.PostAsJsonAsync("/api/mistakes/presigned-url", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<MistakePresignedUrlResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetPresignedUrl failed";
                _logger.LogWarning("GetPresignedUrl failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new MistakePresignedUrlResult();
            }

            return envelope.Data ?? new MistakePresignedUrlResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetPresignedUrl HTTP request failed");
            return new MistakePresignedUrlResult();
        }
    }

    // The opt-in path never exposes upstream messages or degrades failures to empty DTOs.
    private async Task<T> SendStrictAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct,
        bool allowNotFound = false, bool requireData = true) where T : class
    {
        ct.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        try
        {
            using var response = await _httpClient.SendAsync(request, ct);
            ct.ThrowIfCancellationRequested();
            if (request.Options.TryGetValue(MistakeClientPolicy.RequestAborted, out var aborted)) aborted.ThrowIfCancellationRequested();
            if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound) return null!;
            // Only a structurally valid business envelope can preserve these statuses.
            if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Conflict)
            {
                using var failure = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                if (failure.RootElement.ValueKind != JsonValueKind.Object
                    || !failure.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.False)
                    throw new MistakeDownstreamException();
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict) throw new MistakeConflictException();
                throw new MistakeBadRequestException();
            }
            if (!response.IsSuccessStatusCode) throw new MistakeDownstreamException();
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var ok)) throw new MistakeDownstreamException();
            if (ok.ValueKind == JsonValueKind.False) throw new MistakeBadRequestException();
            if (ok.ValueKind != JsonValueKind.True) throw new MistakeDownstreamException();
            if (!requireData)
            {
                // DELETE also publishes {success:true,data:{success:false}} for a
                // business rejection. Optional data must never hide that result.
                if (root.TryGetProperty("data", out var optional))
                {
                    if (optional.ValueKind != JsonValueKind.Object || !optional.TryGetProperty("success", out var deleted))
                        throw new MistakeDownstreamException();
                    if (deleted.ValueKind == JsonValueKind.False) throw new MistakeBadRequestException();
                    if (deleted.ValueKind != JsonValueKind.True) throw new MistakeDownstreamException();
                }
                return null!;
            }
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) throw new MistakeDownstreamException();
            if (data.TryGetProperty("success", out var resultSuccess))
            {
                if (resultSuccess.ValueKind == JsonValueKind.False) throw new MistakeBadRequestException();
                if (resultSuccess.ValueKind != JsonValueKind.True) throw new MistakeDownstreamException();
            }
            var result = data.Deserialize<T>(JsonOptions) ?? throw new MistakeDownstreamException();
            ValidateStrictData(result, data);
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested
            || request.Options.TryGetValue(MistakeClientPolicy.RequestAborted, out var aborted) && aborted.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException or IOException or NotSupportedException)
        {
            // Deliberately omit inner exception, upstream content and URL from all diagnostics.
            throw new MistakeDownstreamException();
        }
    }

    private static void ValidateStrictData<T>(T result, JsonElement data)
    {
        static bool Array(JsonElement value, string name) => value.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.Array;
        static bool Object(JsonElement value, string name) => value.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.Object;
        static bool Nonempty(JsonElement value, string name) => value.TryGetProperty(name, out var member) && member.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(member.GetString());
        static bool Success(JsonElement value) => value.TryGetProperty("success", out var member) && member.ValueKind == JsonValueKind.True;
        var valid = result switch
        {
            MistakeItemPageResult page => Array(data, "items") && Object(data, "pageMeta") && page.Items is not null && page.PageMeta is not null && page.Items.All(ValidItem),
            MistakeItemsByUploadResult items => Array(data, "items") && items.Items is not null && items.Items.All(ValidItem),
            MistakeItemDto item => Nonempty(data, "id") && ValidItem(item),
            SubmitMistakeUploadResult item => Success(data) && Array(data, "createdItemIds") && item.CreatedItemIds is not null,
            CompleteUploadReviewResult item => Success(data) && Array(data, "removedImagePaths") && item.RemovedImagePaths is not null,
            MistakePresignedUrlResult => Nonempty(data, "url"),
            _ => false
        };
        if (!valid) throw new MistakeDownstreamException();
    }

    private static bool ValidItem(MistakeItemDto item) => item is not null && !string.IsNullOrWhiteSpace(item.Id)
        && item.SourceRegions is not null && item.SourceRegions.All(region => region is not null && region.SourceImagePath is not null);

    // ===== Envelope for deserializing { success, data, message } responses =====

    private class ApiEnvelope<T>
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public T? Data { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}

// ===== DI extension =====

public static class MistakeHttpClientServiceCollectionExtensions
{
    public static IServiceCollection AddMistakeHttpClient(this IServiceCollection services, string baseAddress)
    {
        services.AddHttpClient<IMistakeHttpClient, MistakeHttpClient>((sp, client) =>
        {
            client.BaseAddress = new Uri(baseAddress);
        });
        return services;
    }
}
