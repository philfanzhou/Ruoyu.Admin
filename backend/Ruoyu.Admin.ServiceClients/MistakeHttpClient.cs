using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ruoyu.Admin.ServiceClients;

// ===== Mistake service DTOs =====
// These DTOs mirror the HTTP response shapes published by the Mistake service, which lives in
// the separate Ruoyu.Study repository (https://github.com/philfanzhou/Ruoyu.Study). They are
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

public enum MistakeProcessAction
{
    Unspecified = 0,
    Created = 1,
    Analyzed = 2,
    ReviewConfirmed = 3,
    ReviewRejected = 4,
    Practiced = 5,
    Mastered = 6,
    Reanalyzed = 7,
}

public enum MistakeReanalyzeJobStatus
{
    Unspecified = 0,
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
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

public class PendingReviewUploadDto
{
    public string SourceUploadId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public int Subject { get; set; }
    public int Grade { get; set; }
    public int PendingCount { get; set; }
    public int TotalCount { get; set; }
    public string EarliestCreatedAt { get; set; } = string.Empty;
}

public class PendingReviewUploadListResult
{
    public List<PendingReviewUploadDto> Uploads { get; set; } = new();
    public MistakePageMetaDto PageMeta { get; set; } = new();
}

public class ReviewMistakeItemResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string SourceUploadId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public List<string> RemovedImagePaths { get; set; } = new();
}

public class AddMistakeItemResult
{
    public string Id { get; set; } = string.Empty;
}

public class DeleteMistakeItemResult
{
    public bool Success { get; set; }
}

public class ReanalyzeMistakeItemResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
}

public class ReanalyzeJobDto
{
    public string JobId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public MistakeReanalyzeJobStatus Status { get; set; }
    public string NewRootCause { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string StartedAt { get; set; } = string.Empty;
    public string CompletedAt { get; set; } = string.Empty;
    public long VlDurationMs { get; set; }
}

public class GetReanalyzeJobsResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public List<ReanalyzeJobDto> Jobs { get; set; } = new();
}

public class GetActiveReanalyzeJobsResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public List<ReanalyzeJobDto> Jobs { get; set; } = new();
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
    Task<PendingReviewUploadListResult> GetPendingReviewUploadsAsync(int page, int size, IReadOnlyList<int>? subjects, CancellationToken ct = default);
    Task<ReviewMistakeItemResult> ReviewMistakeItemAsync(string id, MistakeReviewStatus reviewStatus, string reviewerId, string? rootCause, int grade, int subject, string? returnReason, CancellationToken ct = default);
    Task<AddMistakeItemResult> AddMistakeItemAsync(string studentId, int subject, int grade, string sourceUploadId, string? rootCause, IReadOnlyList<MistakeSourceRegionDto> sourceRegions, CancellationToken ct = default);
    Task<DeleteMistakeItemResult> DeleteMistakeItemAsync(string id, CancellationToken ct = default);
    Task<ReanalyzeMistakeItemResult> ReanalyzeMistakeItemAsync(string id, string reviewerId, string studentId, string? teacherDescription, CancellationToken ct = default);
    Task<GetReanalyzeJobsResult> GetReanalyzeJobsAsync(string reviewerId, IReadOnlyList<string> jobIds, CancellationToken ct = default);
    Task<GetActiveReanalyzeJobsResult> GetActiveReanalyzeJobsAsync(string reviewerId, CancellationToken ct = default);
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public MistakeHttpClient(HttpClient httpClient, ILogger<MistakeHttpClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    // 1. GET /api/mistakes
    public async Task<MistakeItemPageResult> GetMistakeItemListAsync(string studentId, int subject, int grade, MistakeReviewStatus reviewStatus, int page, int size, CancellationToken ct = default)
    {
        try
        {
            var query = $"?page={page}&size={size}&subject={subject}&grade={grade}&reviewStatus={(int)reviewStatus}";
            if (!string.IsNullOrWhiteSpace(studentId))
                query += $"&studentId={Uri.EscapeDataString(studentId)}";

            using var response = await _httpClient.GetAsync($"/api/mistakes{query}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<MistakeItemPageResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetMistakeItemList failed";
                _logger.LogWarning("GetMistakeItemList failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new MistakeItemPageResult();
            }

            return envelope.Data ?? new MistakeItemPageResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetMistakeItemList HTTP request failed");
            return new MistakeItemPageResult();
        }
    }

    // 2. GET /api/mistakes/{id}
    public async Task<MistakeItemDto?> GetMistakeItemAsync(string id, CancellationToken ct = default)
    {
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

    // 4. GET /api/mistakes/pending-reviews
    public async Task<PendingReviewUploadListResult> GetPendingReviewUploadsAsync(int page, int size, IReadOnlyList<int>? subjects, CancellationToken ct = default)
    {
        try
        {
            var query = $"?page={page}&size={size}";
            if (subjects != null && subjects.Count > 0)
                query += $"&subjects={Uri.EscapeDataString(string.Join(",", subjects))}";

            using var response = await _httpClient.GetAsync($"/api/mistakes/pending-reviews{query}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PendingReviewUploadListResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetPendingReviewUploads failed";
                _logger.LogWarning("GetPendingReviewUploads failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new PendingReviewUploadListResult();
            }

            return envelope.Data ?? new PendingReviewUploadListResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetPendingReviewUploads HTTP request failed");
            return new PendingReviewUploadListResult();
        }
    }

    // 5. POST /api/mistakes/{id}/review
    public async Task<ReviewMistakeItemResult> ReviewMistakeItemAsync(string id, MistakeReviewStatus reviewStatus, string reviewerId, string? rootCause, int grade, int subject, string? returnReason, CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                id,
                reviewStatus,
                reviewerId,
                rootCause,
                grade,
                subject,
                returnReason
            };
            using var response = await _httpClient.PostAsJsonAsync($"/api/mistakes/{id}/review", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<ReviewMistakeItemResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "ReviewMistakeItem failed";
                _logger.LogWarning("ReviewMistakeItem failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new ReviewMistakeItemResult { Success = false, ErrorMessage = msg };
            }

            return envelope.Data ?? new ReviewMistakeItemResult { Success = false, ErrorMessage = "Empty response data" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "ReviewMistakeItem HTTP request failed");
            return new ReviewMistakeItemResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // 6. POST /api/mistakes
    public async Task<AddMistakeItemResult> AddMistakeItemAsync(string studentId, int subject, int grade, string sourceUploadId, string? rootCause, IReadOnlyList<MistakeSourceRegionDto> sourceRegions, CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                studentId,
                subject,
                grade,
                sourceUploadId,
                rootCause,
                sourceRegions
            };
            using var response = await _httpClient.PostAsJsonAsync("/api/mistakes", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<MistakeItemDto>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "AddMistakeItem failed";
                _logger.LogWarning("AddMistakeItem failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new AddMistakeItemResult();
            }

            var item = envelope.Data;
            return new AddMistakeItemResult { Id = item?.Id ?? string.Empty };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "AddMistakeItem HTTP request failed");
            return new AddMistakeItemResult();
        }
    }

    // 7. DELETE /api/mistakes/{id}
    public async Task<DeleteMistakeItemResult> DeleteMistakeItemAsync(string id, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.DeleteAsync($"/api/mistakes/{id}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "DeleteMistakeItem failed";
                _logger.LogWarning("DeleteMistakeItem failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new DeleteMistakeItemResult { Success = false };
            }

            return new DeleteMistakeItemResult { Success = true };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "DeleteMistakeItem HTTP request failed");
            return new DeleteMistakeItemResult { Success = false };
        }
    }

    // 8. POST /api/mistakes/{id}/reanalyze
    public async Task<ReanalyzeMistakeItemResult> ReanalyzeMistakeItemAsync(string id, string reviewerId, string studentId, string? teacherDescription, CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                id,
                reviewerId,
                studentId,
                teacherDescription
            };
            using var response = await _httpClient.PostAsJsonAsync($"/api/mistakes/{id}/reanalyze", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<ReanalyzeMistakeItemResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "ReanalyzeMistakeItem failed";
                _logger.LogWarning("ReanalyzeMistakeItem failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new ReanalyzeMistakeItemResult { Success = false, ErrorMessage = msg };
            }

            return envelope.Data ?? new ReanalyzeMistakeItemResult { Success = false, ErrorMessage = "Empty response data" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "ReanalyzeMistakeItem HTTP request failed");
            return new ReanalyzeMistakeItemResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // 9. POST /api/mistakes/reanalyze/jobs
    public async Task<GetReanalyzeJobsResult> GetReanalyzeJobsAsync(string reviewerId, IReadOnlyList<string> jobIds, CancellationToken ct = default)
    {
        try
        {
            var body = new { reviewerId, jobIds };
            using var response = await _httpClient.PostAsJsonAsync("/api/mistakes/reanalyze/jobs", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<GetReanalyzeJobsResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetReanalyzeJobs failed";
                _logger.LogWarning("GetReanalyzeJobs failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new GetReanalyzeJobsResult { Success = false, ErrorMessage = msg };
            }

            return envelope.Data ?? new GetReanalyzeJobsResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetReanalyzeJobs HTTP request failed");
            return new GetReanalyzeJobsResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // 10. GET /api/mistakes/reanalyze/active
    public async Task<GetActiveReanalyzeJobsResult> GetActiveReanalyzeJobsAsync(string reviewerId, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/api/mistakes/reanalyze/active?reviewerId={Uri.EscapeDataString(reviewerId)}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<GetActiveReanalyzeJobsResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetActiveReanalyzeJobs failed";
                _logger.LogWarning("GetActiveReanalyzeJobs failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new GetActiveReanalyzeJobsResult { Success = false, ErrorMessage = msg };
            }

            return envelope.Data ?? new GetActiveReanalyzeJobsResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetActiveReanalyzeJobs HTTP request failed");
            return new GetActiveReanalyzeJobsResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    // 11. PUT /api/mistakes/{id}
    public async Task<UpdateMistakeItemResult> UpdateMistakeItemAsync(string id, string studentId, int subject, int grade, IReadOnlyList<MistakeSourceRegionDto>? sourceRegions, CancellationToken ct = default)
    {
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

    // 12. POST /api/mistakes/upload
    public async Task<SubmitMistakeUploadResult> SubmitMistakeUploadAsync(string studentId, int subject, int grade, IReadOnlyList<string> imagePaths, string? rootCause, string sourceUploadId, CancellationToken ct = default)
    {
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

    // 13. POST /api/mistakes/complete-review
    public async Task<CompleteUploadReviewResult> CompleteUploadReviewAsync(string sourceUploadId, string reviewerId, CancellationToken ct = default)
    {
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

    // 14. POST /api/mistakes/presigned-url
    public async Task<MistakePresignedUrlResult> GetPresignedUrlAsync(string objectPath, int expirySeconds, string? size, CancellationToken ct = default)
    {
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
