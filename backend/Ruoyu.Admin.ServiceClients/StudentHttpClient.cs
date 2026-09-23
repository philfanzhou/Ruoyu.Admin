using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ruoyu.Admin.ServiceClients;

// ===== Student service HTTP client and DTOs =====
// These DTOs mirror the HTTP response shapes published by the Student service, which lives in
// the separate Ruoyu.Study repository (https://github.com/philfanzhou/Ruoyu.Study). They are
// hand-maintained copies: this repository has no compile-time reference to the Student service,
// so an upstream contract change surfaces as a deserialization mismatch, not a build error.

// ===== DTOs matching student service HTTP response payload (the "data" portion) =====

public class StageImageResult
{
    public bool Success { get; set; }
    public string StagingPath { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public class HomeworkImagesResult
{
    public bool Success { get; set; }
    public List<string> ImagePaths { get; set; } = new();
    public string ErrorMessage { get; set; } = string.Empty;
}

public class HomeworkImageContentResult
{
    public bool Success { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "image/jpeg";
}

public class ImageEntryDto
{
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = "mistake";
}

public class UploadRecordDto
{
    public string Id { get; set; } = string.Empty;
    public string StudentId { get; set; } = string.Empty;
    public int Status { get; set; }
    public List<ImageEntryDto> ImageEntries { get; set; } = new();
    public List<int> ImageRotations { get; set; } = new();
    public string Comments { get; set; } = string.Empty;
    public string ReturnReason { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

public class DuplicateImageInfoDto
{
    public int ImageIndex { get; set; }
    public string UploadRecordId { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public class UploadImagesResult
{
    public bool Success { get; set; }
    public UploadRecordDto? Record { get; set; }
    public List<DuplicateImageInfoDto> Duplicates { get; set; } = new();
}

public class ImageExistenceInfoDto
{
    public int ImageIndex { get; set; }
    public bool Exists { get; set; }
    public string UploadRecordId { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public class CheckImagesExistResult
{
    public List<ImageExistenceInfoDto> Items { get; set; } = new();
}

public class PresignedUrlResult
{
    public string Url { get; set; } = string.Empty;
    public int ExpirySeconds { get; set; }
}

public class UploadRecordsPage
{
    public List<UploadRecordDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class RemoveImageResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool RecordDeleted { get; set; }
    public int RemainingImageCount { get; set; }
}

public class DeleteRecordResult
{
    public bool Success { get; set; }
}

public class AppendImagesResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public UploadRecordDto? Record { get; set; }
    public List<DuplicateImageInfoDto> Duplicates { get; set; } = new();
}

public class ResetStatusResult
{
    public bool Success { get; set; }
}

public class StudentInfoDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Grade { get; set; }
    public bool IsDefault { get; set; }
    public List<string> IdentityAccountIds { get; set; } = new();
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}

public class StudentsByAccountResult
{
    public List<StudentInfoDto> Students { get; set; } = new();
}

public class SetDefaultResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class OpenSubjectItemDto
{
    public string Id { get; set; } = string.Empty;
    public int Subject { get; set; }
    public string OpenStartDate { get; set; } = string.Empty;
    public string OpenEndDate { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class OpenSubjectsResult
{
    public List<OpenSubjectItemDto> Subjects { get; set; } = new();
}

// ===== DTOs for Ruoyu.Admin student/upload management =====

public class StudentDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Grade { get; set; }
    public List<string> IdentityAccountIds { get; set; } = new();
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public bool IsDefault { get; set; }
}

public class PagedStudentsResult
{
    public List<StudentDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class SetOpenSubjectItem
{
    public int Subject { get; set; }
    public string OpenStartDate { get; set; } = string.Empty;
    public string? OpenEndDate { get; set; }
}

public class SubjectOptionDto
{
    public int Value { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class AvailableSubjectsResult
{
    public List<SubjectOptionDto> Subjects { get; set; } = new();
}

public class RemoveImagesResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool RecordDeleted { get; set; }
    public int RemainingImageCount { get; set; }
}

// ===== DTOs for mistake→student calls (added per ADR-001-mistake-webapi-only) =====

public class CreateReturnedRecordRequest
{
    public List<ImageEntryDto> ImageEntries { get; set; } = new();
    public string? Comments { get; set; }
    public string? ReturnReason { get; set; }
}

public class RemoveProcessedImagesResult
{
    public bool Success { get; set; }
    public bool RecordDeleted { get; set; }
    public int RemainingImageCount { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class AccountIdsResult
{
    public List<string> AccountIds { get; set; } = new();
}

// ===== Interface =====

public interface IStudentHttpClient
{
    Task<StageImageResult> StageImageAsync(byte[] imageData, string contentType, CancellationToken ct = default);
    Task<HomeworkImagesResult> PromoteHomeworkImagesAsync(string studentId, string homeworkId, List<string> partIds, CancellationToken ct = default);
    Task DeleteHomeworkImagesAsync(string studentId, string homeworkId, List<string> imagePaths, CancellationToken ct = default);
    Task<HomeworkImageContentResult> DownloadHomeworkImageAsync(string objectPath, CancellationToken ct = default);
    Task<UploadImagesResult> UploadImagesByPathsAsync(string studentId, List<string> stagingPaths, List<string>? clientHashes, string? comments, CancellationToken ct = default);
    Task<CheckImagesExistResult> CheckImagesExistAsync(string studentId, List<string> imageHashes, CancellationToken ct = default);
    Task<PresignedUrlResult> GetPresignedUrlAsync(string objectPath, int expirySeconds, string? size, CancellationToken ct = default);
    Task<UploadRecordsPage> GetUploadRecordsAsync(string studentId, int status, int page, int pageSize, CancellationToken ct = default);
    Task<UploadRecordDto> GetUploadRecordAsync(string studentId, string recordId, CancellationToken ct = default);
    Task<RemoveImageResult> RemoveImageFromRecordAsync(string studentId, string recordId, int imageIndex, CancellationToken ct = default);
    Task<DeleteRecordResult> DeleteUploadRecordAsync(string studentId, string recordId, bool deleteOssFiles, CancellationToken ct = default);
    Task<AppendImagesResult> AppendImagesToRecordAsync(string studentId, string recordId, List<string> stagingPaths, List<string>? clientHashes, CancellationToken ct = default);
    Task<ResetStatusResult> ResetUploadRecordStatusAsync(string studentId, string recordId, int targetStatus, CancellationToken ct = default);
    Task MarkUploadRecordAsReturnedAsync(string studentId, string recordId, string? comments, string? returnReason, CancellationToken ct = default);
    Task<StudentsByAccountResult> GetStudentsByIdentityAccountIdAsync(string accountId, CancellationToken ct = default);
    Task<SetDefaultResult> SetDefaultStudentAsync(string studentId, string identityAccountId, CancellationToken ct = default);
    Task<OpenSubjectsResult> GetStudentOpenSubjectsAsync(string studentId, bool activeOnly, CancellationToken ct = default);

    // ===== Admin portal extensions =====
    Task<StudentDto> GetStudentAsync(string studentId, CancellationToken ct = default);
    Task<PagedStudentsResult> ListStudentsAsync(int? grade, int page, int pageSize, string? keyword, CancellationToken ct = default);
    Task<StudentDto> CreateStudentAsync(string name, int grade, List<string> identityAccountIds, CancellationToken ct = default);
    Task<StudentDto> UpdateStudentAsync(string studentId, string name, int grade, List<string>? identityAccountIds, CancellationToken ct = default);
    Task DeleteStudentAsync(string studentId, CancellationToken ct = default);
    Task<List<string>> GetIdentityAccountsByStudentIdAsync(string studentId, CancellationToken ct = default);
    Task LinkIdentityAccountToStudentAsync(string studentId, string accountId, CancellationToken ct = default);
    Task UnlinkIdentityAccountFromStudentAsync(string studentId, string accountId, CancellationToken ct = default);
    Task SetStudentOpenSubjectsAsync(string studentId, List<SetOpenSubjectItem> subjects, CancellationToken ct = default);
    Task<AvailableSubjectsResult> GetAvailableSubjectsAsync(CancellationToken ct = default);
    Task<UploadRecordsPage> GetAllUploadRecordsAsync(int? status, int page, int pageSize, string? studentId, CancellationToken ct = default);
    Task RotateUploadImageAsync(string studentId, string recordId, int imageIndex, int rotation, CancellationToken ct = default);
    Task<RemoveImagesResult> RemoveImagesFromRecordAsync(string studentId, string recordId, List<string> imagePaths, CancellationToken ct = default);
    Task DeleteUploadRecordAfterReviewAsync(string studentId, string recordId, CancellationToken ct = default);

    // ===== Mistake service extensions (mistake→student calls per ADR-001-mistake-webapi-only) =====
    Task CreateReturnedRecordAsync(string studentId, List<ImageEntryDto> imageEntries, string? comments, string? returnReason, CancellationToken ct = default);
    Task<UploadRecordsPage> GetProcessingUploadRecordsAsync(int page, int pageSize, CancellationToken ct = default);
    Task<RemoveProcessedImagesResult> RemoveProcessedImagesAsync(string recordId, List<string> imagePaths, CancellationToken ct = default);
}

// ===== Implementation =====

public class StudentHttpClient : IStudentHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StudentHttpClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public StudentHttpClient(HttpClient httpClient, ILogger<StudentHttpClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<StageImageResult> StageImageAsync(byte[] imageData, string contentType, CancellationToken ct = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(imageData);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType);
            form.Add(fileContent, "file", "upload.jpg");

            using var response = await _httpClient.PostAsync("/api/oss/stage-image", form, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<StageImageResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "StageImage failed";
                _logger.LogWarning("StageImage failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new StageImageResult { Success = false, ErrorMessage = msg };
            }

            return envelope.Data ?? new StageImageResult { Success = false, ErrorMessage = "Empty response data" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "StageImage HTTP request failed");
            return new StageImageResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<HomeworkImagesResult> PromoteHomeworkImagesAsync(
        string studentId, string homeworkId, List<string> partIds, CancellationToken ct = default)
    {
        try
        {
            var body = new { studentId, homeworkId, partIds };
            using var response = await _httpClient.PostAsJsonAsync("/api/oss/homework-images/promote", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<HomeworkImagesResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var message = envelope?.Message ?? "PromoteHomeworkImages failed";
                _logger.LogWarning("PromoteHomeworkImages failed: {Message}, Status: {Status}", message, response.StatusCode);
                return new HomeworkImagesResult { Success = false, ErrorMessage = message };
            }
            return envelope.Data ?? new HomeworkImagesResult { Success = false, ErrorMessage = "Empty response data" };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogError(ex, "PromoteHomeworkImages request failed");
            return new HomeworkImagesResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task DeleteHomeworkImagesAsync(
        string studentId, string homeworkId, List<string> imagePaths, CancellationToken ct = default)
    {
        try
        {
            var body = new { studentId, homeworkId, imagePaths };
            using var response = await _httpClient.PostAsJsonAsync("/api/oss/homework-images/delete", body, JsonOptions, ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("DeleteHomeworkImages compensation failed with status {Status}", response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "DeleteHomeworkImages compensation request failed");
        }
    }

    public async Task<HomeworkImageContentResult> DownloadHomeworkImageAsync(
        string objectPath, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                $"/api/oss/homework-image-content?objectPath={Uri.EscapeDataString(objectPath)}", ct);
            if (!response.IsSuccessStatusCode) return new HomeworkImageContentResult();
            return new HomeworkImageContentResult
            {
                Success = true,
                Data = await response.Content.ReadAsByteArrayAsync(ct),
                ContentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Homework image download failed");
            return new HomeworkImageContentResult();
        }
    }

    public async Task<UploadImagesResult> UploadImagesByPathsAsync(string studentId, List<string> stagingPaths, List<string>? clientHashes, string? comments, CancellationToken ct = default)
    {
        try
        {
            var body = new { stagingPaths, clientHashes, comments };
            using var response = await _httpClient.PostAsJsonAsync($"/api/students/{studentId}/uploads/by-paths", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<UploadImagesResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "UploadImagesByPaths failed";
                _logger.LogWarning("UploadImagesByPaths failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new UploadImagesResult { Success = false };
            }

            return envelope.Data ?? new UploadImagesResult { Success = false };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "UploadImagesByPaths HTTP request failed");
            return new UploadImagesResult { Success = false };
        }
    }

    public async Task<CheckImagesExistResult> CheckImagesExistAsync(string studentId, List<string> imageHashes, CancellationToken ct = default)
    {
        try
        {
            var body = new { imageHashes };
            using var response = await _httpClient.PostAsJsonAsync($"/api/students/{studentId}/uploads/check-images", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CheckImagesExistResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "CheckImagesExist failed";
                _logger.LogWarning("CheckImagesExist failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new CheckImagesExistResult();
            }

            return envelope.Data ?? new CheckImagesExistResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "CheckImagesExist HTTP request failed");
            return new CheckImagesExistResult();
        }
    }

    public async Task<PresignedUrlResult> GetPresignedUrlAsync(string objectPath, int expirySeconds, string? size, CancellationToken ct = default)
    {
        try
        {
            var body = new { objectPath, expirySeconds, size };
            using var response = await _httpClient.PostAsJsonAsync("/api/oss/presigned-url", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PresignedUrlResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetPresignedUrl failed";
                _logger.LogWarning("GetPresignedUrl failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new PresignedUrlResult();
            }

            return envelope.Data ?? new PresignedUrlResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetPresignedUrl HTTP request failed");
            return new PresignedUrlResult();
        }
    }

    public async Task<UploadRecordsPage> GetUploadRecordsAsync(string studentId, int status, int page, int pageSize, CancellationToken ct = default)
    {
        try
        {
            var url = $"/api/students/{studentId}/uploads?status={status}&page={page}&pageSize={pageSize}";
            using var response = await _httpClient.GetAsync(url, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<UploadRecordsPage>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetUploadRecords failed";
                _logger.LogWarning("GetUploadRecords failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new UploadRecordsPage();
            }

            return envelope.Data ?? new UploadRecordsPage();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetUploadRecords HTTP request failed");
            return new UploadRecordsPage();
        }
    }

    public async Task<UploadRecordDto> GetUploadRecordAsync(string studentId, string recordId, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/api/students/{studentId}/uploads/{recordId}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<UploadRecordDto>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetUploadRecord failed";
                _logger.LogWarning("GetUploadRecord failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new UploadRecordDto();
            }

            return envelope.Data ?? new UploadRecordDto();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetUploadRecord HTTP request failed");
            return new UploadRecordDto();
        }
    }

    public async Task<RemoveImageResult> RemoveImageFromRecordAsync(string studentId, string recordId, int imageIndex, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.DeleteAsync($"/api/students/{studentId}/uploads/{recordId}/images/{imageIndex}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<RemoveImageResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "RemoveImageFromRecord failed";
                _logger.LogWarning("RemoveImageFromRecord failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new RemoveImageResult { Success = false, Message = msg };
            }

            return envelope.Data ?? new RemoveImageResult { Success = false };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "RemoveImageFromRecord HTTP request failed");
            return new RemoveImageResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<DeleteRecordResult> DeleteUploadRecordAsync(string studentId, string recordId, bool deleteOssFiles, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.DeleteAsync($"/api/students/{studentId}/uploads/{recordId}?deleteOssFiles={deleteOssFiles.ToString().ToLowerInvariant()}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "DeleteUploadRecord failed";
                _logger.LogWarning("DeleteUploadRecord failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new DeleteRecordResult { Success = false };
            }

            return new DeleteRecordResult { Success = true };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "DeleteUploadRecord HTTP request failed");
            return new DeleteRecordResult { Success = false };
        }
    }

    public async Task<AppendImagesResult> AppendImagesToRecordAsync(string studentId, string recordId, List<string> stagingPaths, List<string>? clientHashes, CancellationToken ct = default)
    {
        try
        {
            var body = new { stagingPaths, clientHashes };
            using var response = await _httpClient.PostAsJsonAsync($"/api/students/{studentId}/uploads/{recordId}/append", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AppendImagesResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "AppendImagesToRecord failed";
                _logger.LogWarning("AppendImagesToRecord failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new AppendImagesResult { Success = false, ErrorMessage = msg };
            }

            return envelope.Data ?? new AppendImagesResult { Success = false };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "AppendImagesToRecord HTTP request failed");
            return new AppendImagesResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<ResetStatusResult> ResetUploadRecordStatusAsync(string studentId, string recordId, int targetStatus, CancellationToken ct = default)
    {
        try
        {
            var body = new { targetStatus };
            using var response = await _httpClient.PostAsJsonAsync($"/api/students/{studentId}/uploads/{recordId}/reset-status", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "ResetUploadRecordStatus failed";
                _logger.LogWarning("ResetUploadRecordStatus failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new ResetStatusResult { Success = false };
            }

            return new ResetStatusResult { Success = true };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "ResetUploadRecordStatus HTTP request failed");
            return new ResetStatusResult { Success = false };
        }
    }

    public async Task MarkUploadRecordAsReturnedAsync(string studentId, string recordId, string? comments, string? returnReason, CancellationToken ct = default)
    {
        try
        {
            var body = new { comments, returnReason };
            using var response = await _httpClient.PostAsJsonAsync($"/api/students/{studentId}/uploads/{recordId}/mark-returned", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "MarkUploadRecordAsReturned failed";
                _logger.LogWarning("MarkUploadRecordAsReturned failed: {Message}, Status: {Status}", msg, response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "MarkUploadRecordAsReturned HTTP request failed");
        }
    }

    public async Task<StudentsByAccountResult> GetStudentsByIdentityAccountIdAsync(string accountId, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/api/accounts/{accountId}/students", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<StudentsByAccountResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetStudentsByIdentityAccountId failed";
                _logger.LogWarning("GetStudentsByIdentityAccountId failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new StudentsByAccountResult();
            }

            return envelope.Data ?? new StudentsByAccountResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetStudentsByIdentityAccountId HTTP request failed");
            return new StudentsByAccountResult();
        }
    }

    public async Task<SetDefaultResult> SetDefaultStudentAsync(string studentId, string identityAccountId, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PutAsync($"/api/students/{studentId}/default-account/{identityAccountId}", content: null, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "SetDefaultStudent failed";
                _logger.LogWarning("SetDefaultStudent failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new SetDefaultResult { Success = false, ErrorMessage = msg };
            }

            return new SetDefaultResult { Success = true };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "SetDefaultStudent HTTP request failed");
            return new SetDefaultResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<OpenSubjectsResult> GetStudentOpenSubjectsAsync(string studentId, bool activeOnly, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/api/students/{studentId}/subjects?activeOnly={activeOnly.ToString().ToLowerInvariant()}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<OpenSubjectsResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetStudentOpenSubjects failed";
                _logger.LogWarning("GetStudentOpenSubjects failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new OpenSubjectsResult();
            }

            return envelope.Data ?? new OpenSubjectsResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetStudentOpenSubjects HTTP request failed");
            return new OpenSubjectsResult();
        }
    }

    public async Task<StudentDto> GetStudentAsync(string studentId, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/api/students/{studentId}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<StudentDto>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetStudent failed";
                _logger.LogWarning("GetStudent failed: {Message}, Status: {Status}", msg, response.StatusCode);
                throw new HttpRequestException(msg, null, response.StatusCode);
            }

            return envelope.Data ?? new StudentDto();
        }
        catch (HttpRequestException)
        {
            throw;
        }
    }

    public async Task<PagedStudentsResult> ListStudentsAsync(int? grade, int page, int pageSize, string? keyword, CancellationToken ct = default)
    {
        try
        {
            var query = $"?page={page}&pageSize={pageSize}";
            if (grade.HasValue && grade.Value > 0)
                query += $"&grade={grade.Value}";
            if (!string.IsNullOrWhiteSpace(keyword))
                query += $"&keyword={Uri.EscapeDataString(keyword)}";

            using var response = await _httpClient.GetAsync($"/api/students{query}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PagedStudentsResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "ListStudents failed";
                _logger.LogWarning("ListStudents failed: {Message}, Status: {Status}", msg, response.StatusCode);
                throw new HttpRequestException(msg, null, response.StatusCode);
            }

            return envelope.Data ?? new PagedStudentsResult();
        }
        catch (HttpRequestException)
        {
            throw;
        }
    }

    public async Task<StudentDto> CreateStudentAsync(string name, int grade, List<string> identityAccountIds, CancellationToken ct = default)
    {
        var body = new { name, grade, identityAccountIds };
        using var response = await _httpClient.PostAsJsonAsync("/api/students", body, JsonOptions, ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<StudentDto>>(JsonOptions, ct);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            var msg = envelope?.Message ?? "CreateStudent failed";
            _logger.LogWarning("CreateStudent failed: {Message}, Status: {Status}", msg, response.StatusCode);
            throw new HttpRequestException(msg, null, response.StatusCode);
        }

        return envelope.Data ?? new StudentDto();
    }

    public async Task<StudentDto> UpdateStudentAsync(string studentId, string name, int grade, List<string>? identityAccountIds, CancellationToken ct = default)
    {
        var body = new { name, grade, identityAccountIds };
        using var response = await _httpClient.PutAsJsonAsync($"/api/students/{studentId}", body, JsonOptions, ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<StudentDto>>(JsonOptions, ct);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            var msg = envelope?.Message ?? "UpdateStudent failed";
            _logger.LogWarning("UpdateStudent failed: {Message}, Status: {Status}", msg, response.StatusCode);
            throw new HttpRequestException(msg, null, response.StatusCode);
        }

        return envelope.Data ?? new StudentDto();
    }

    public async Task DeleteStudentAsync(string studentId, CancellationToken ct = default)
    {
        using var response = await _httpClient.DeleteAsync($"/api/students/{studentId}", ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            var msg = envelope?.Message ?? "DeleteStudent failed";
            _logger.LogWarning("DeleteStudent failed: {Message}, Status: {Status}", msg, response.StatusCode);
            throw new HttpRequestException(msg, null, response.StatusCode);
        }
    }

    public async Task<List<string>> GetIdentityAccountsByStudentIdAsync(string studentId, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/api/students/{studentId}/accounts", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AccountIdsResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetIdentityAccountsByStudentId failed";
                _logger.LogWarning("GetIdentityAccountsByStudentId failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new List<string>();
            }

            return envelope.Data?.AccountIds ?? new List<string>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetIdentityAccountsByStudentId HTTP request failed");
            return new List<string>();
        }
    }

    public async Task LinkIdentityAccountToStudentAsync(string studentId, string accountId, CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsync($"/api/students/{studentId}/accounts/{accountId}", content: null, ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            var msg = envelope?.Message ?? "LinkIdentityAccountToStudent failed";
            _logger.LogWarning("LinkIdentityAccountToStudent failed: {Message}, Status: {Status}", msg, response.StatusCode);
            throw new HttpRequestException(msg, null, response.StatusCode);
        }
    }

    public async Task UnlinkIdentityAccountFromStudentAsync(string studentId, string accountId, CancellationToken ct = default)
    {
        using var response = await _httpClient.DeleteAsync($"/api/students/{studentId}/accounts/{accountId}", ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            var msg = envelope?.Message ?? "UnlinkIdentityAccountFromStudent failed";
            _logger.LogWarning("UnlinkIdentityAccountFromStudent failed: {Message}, Status: {Status}", msg, response.StatusCode);
            throw new HttpRequestException(msg, null, response.StatusCode);
        }
    }

    public async Task SetStudentOpenSubjectsAsync(string studentId, List<SetOpenSubjectItem> subjects, CancellationToken ct = default)
    {
        var body = new { subjects };
        using var response = await _httpClient.PutAsJsonAsync($"/api/students/{studentId}/subjects", body, JsonOptions, ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            var msg = envelope?.Message ?? "SetStudentOpenSubjects failed";
            _logger.LogWarning("SetStudentOpenSubjects failed: {Message}, Status: {Status}", msg, response.StatusCode);
            throw new HttpRequestException(msg, null, response.StatusCode);
        }
    }

    public async Task<AvailableSubjectsResult> GetAvailableSubjectsAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("/api/subjects/available", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AvailableSubjectsResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetAvailableSubjects failed";
                _logger.LogWarning("GetAvailableSubjects failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new AvailableSubjectsResult();
            }

            return envelope.Data ?? new AvailableSubjectsResult();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetAvailableSubjects HTTP request failed");
            return new AvailableSubjectsResult();
        }
    }

    public async Task<UploadRecordsPage> GetAllUploadRecordsAsync(int? status, int page, int pageSize, string? studentId, CancellationToken ct = default)
    {
        var query = $"?page={page}&pageSize={pageSize}";
        if (status.HasValue && status.Value >= 0)
            query += $"&status={status.Value}";
        if (!string.IsNullOrWhiteSpace(studentId))
            query += $"&studentId={Uri.EscapeDataString(studentId)}";

        using var response = await _httpClient.GetAsync($"/api/uploads{query}", ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<UploadRecordsPage>>(JsonOptions, ct);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            var msg = envelope?.Message ?? "GetAllUploadRecords failed";
            _logger.LogWarning("GetAllUploadRecords failed: {Message}, Status: {Status}", msg, response.StatusCode);
            throw new HttpRequestException(msg, null, response.StatusCode);
        }

        return envelope.Data ?? new UploadRecordsPage();
    }

    public async Task RotateUploadImageAsync(string studentId, string recordId, int imageIndex, int rotation, CancellationToken ct = default)
    {
        var body = new { imageIndex, rotation };
        using var response = await _httpClient.PostAsJsonAsync($"/api/students/{studentId}/uploads/{recordId}/rotate", body, JsonOptions, ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            var msg = envelope?.Message ?? "RotateUploadImage failed";
            _logger.LogWarning("RotateUploadImage failed: {Message}, Status: {Status}", msg, response.StatusCode);
            throw new HttpRequestException(msg, null, response.StatusCode);
        }
    }

    public async Task<RemoveImagesResult> RemoveImagesFromRecordAsync(string studentId, string recordId, List<string> imagePaths, CancellationToken ct = default)
    {
        try
        {
            var body = new { imagePaths };
            using var response = await _httpClient.PostAsJsonAsync($"/api/students/{studentId}/uploads/{recordId}/remove-images", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<RemoveImagesResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "RemoveImagesFromRecord failed";
                _logger.LogWarning("RemoveImagesFromRecord failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new RemoveImagesResult { Success = false, ErrorMessage = msg };
            }

            return envelope.Data ?? new RemoveImagesResult { Success = false };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "RemoveImagesFromRecord HTTP request failed");
            return new RemoveImagesResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task DeleteUploadRecordAfterReviewAsync(string studentId, string recordId, CancellationToken ct = default)
    {
        using var response = await _httpClient.DeleteAsync($"/api/students/{studentId}/uploads/{recordId}/after-review", ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
        if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
        {
            var msg = envelope?.Message ?? "DeleteUploadRecordAfterReview failed";
            _logger.LogWarning("DeleteUploadRecordAfterReview failed: {Message}, Status: {Status}", msg, response.StatusCode);
            throw new HttpRequestException(msg, null, response.StatusCode);
        }
    }

    // ===== Mistake service extensions (mistake→student calls per ADR-001-mistake-webapi-only) =====

    public async Task CreateReturnedRecordAsync(string studentId, List<ImageEntryDto> imageEntries, string? comments, string? returnReason, CancellationToken ct = default)
    {
        try
        {
            var body = new CreateReturnedRecordRequest
            {
                ImageEntries = imageEntries,
                Comments = comments,
                ReturnReason = returnReason
            };
            using var response = await _httpClient.PostAsJsonAsync($"/api/students/{studentId}/uploads/returned", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "CreateReturnedRecord failed";
                _logger.LogWarning("CreateReturnedRecord failed: {Message}, Status: {Status}", msg, response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "CreateReturnedRecord HTTP request failed");
        }
    }

    public async Task<UploadRecordsPage> GetProcessingUploadRecordsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/api/uploads/processing?page={page}&pageSize={pageSize}", ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<UploadRecordsPage>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "GetProcessingUploadRecords failed";
                _logger.LogWarning("GetProcessingUploadRecords failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new UploadRecordsPage();
            }

            return envelope.Data ?? new UploadRecordsPage();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "GetProcessingUploadRecords HTTP request failed");
            return new UploadRecordsPage();
        }
    }

    public async Task<RemoveProcessedImagesResult> RemoveProcessedImagesAsync(string recordId, List<string> imagePaths, CancellationToken ct = default)
    {
        try
        {
            var body = new { imagePaths };
            using var response = await _httpClient.PostAsJsonAsync($"/api/uploads/{recordId}/remove-processed", body, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<RemoveProcessedImagesResult>>(JsonOptions, ct);
            if (!response.IsSuccessStatusCode || envelope is null || !envelope.Success)
            {
                var msg = envelope?.Message ?? "RemoveProcessedImages failed";
                _logger.LogWarning("RemoveProcessedImages failed: {Message}, Status: {Status}", msg, response.StatusCode);
                return new RemoveProcessedImagesResult { Success = false, ErrorMessage = msg };
            }

            return envelope.Data ?? new RemoveProcessedImagesResult { Success = false };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "RemoveProcessedImages HTTP request failed");
            return new RemoveProcessedImagesResult { Success = false, ErrorMessage = ex.Message };
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

public static class StudentHttpClientServiceCollectionExtensions
{
    public static IServiceCollection AddStudentHttpClient(this IServiceCollection services, string baseAddress)
    {
        services.AddHttpClient<IStudentHttpClient, StudentHttpClient>((sp, client) =>
        {
            client.BaseAddress = new Uri(baseAddress);
        });
        return services;
    }
}
