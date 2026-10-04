using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ruoyu.Admin.ServiceClients;

/// <summary>Only the managed upload path accepts a request-scoped, already authenticated caller token.</summary>
public sealed class ManagedMistakeHttpClient(HttpClient client) : IManagedMistakeHttpClient
{
    private const int MaxResponseBytes = 65536;
    private static readonly HashSet<string> KnownErrors = new(StringComparer.Ordinal)
    {
        "invalid_request", "invalid_condition", "invalid_region", "invalid_source", "source_not_found",
        "manifest_conflict", "student_conflict", "student_binding", "payload_conflict", "request_payload_conflict", "grouping_conflict",
        "source_sealed", "source_managed", "lease_lost", "authentication_required", "forbidden",
        "legacy_source_conflict", "intake_disabled", "source_unavailable", "handoff_pending", "source_mode_conflict", "intake_busy"
    };

    public async Task<ManagedMistakeResult> SubmitAsync(ManagedMistakeUpload upload, string accessToken, CancellationToken ct)
    {
        if (client.BaseAddress?.Scheme != "https" || string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Managed upload requires HTTPS and an authenticated caller.");
        if (ct.IsCancellationRequested) return ManagedMistakeResult.Unknown("cancelled");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var cancellation = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/mistakes/upload")
        {
            Content = JsonContent.Create(new
            {
                sourceUploadId = upload.SourceUploadId, expectedContentRevision = upload.ExpectedContentRevision,
                requestKey = upload.RequestKey, studentId = upload.StudentId, subject = upload.Subject,
                grade = upload.Grade, imagePaths = upload.ImagePaths, rootCause = upload.RootCause
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            var status = (int)response.StatusCode;
            if (status >= 500 || status is >= 300 and < 400)
                return ManagedMistakeResult.Unknown("provider_unavailable", status);
            using var stream = await response.Content.ReadAsStreamAsync(cancellation);
            var buffer = new byte[MaxResponseBytes + 1]; var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), cancellation);
                if (read == 0) break;
                length += read;
            }
            if (length > MaxResponseBytes) return ManagedMistakeResult.Unknown("invalid_response");
            using var document = JsonDocument.Parse(buffer.AsMemory(0, length));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ManagedMistakeResult.Unknown("invalid_response");
            if (root.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
                return ManagedMistakeResult.Unknown("invalid_response");
            var error = root.TryGetProperty("errorKind", out var errorValue) && errorValue.ValueKind == JsonValueKind.String
                && KnownErrors.Contains(errorValue.GetString()!) ? errorValue.GetString()! : "provider_rejected";
            if (status is 400 or 401 or 403 or 404 or 409 or 422) return ManagedMistakeResult.Failed(error, status);
            if (status != 200 || !root.TryGetProperty("success", out var outer)
                || outer.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return ManagedMistakeResult.Unknown("invalid_response");
            if (!outer.GetBoolean()) return ManagedMistakeResult.Failed(error, status);
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("success", out var inner) || inner.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return ManagedMistakeResult.Unknown("invalid_response");
            if (!inner.GetBoolean()) return ManagedMistakeResult.Failed("provider_rejected", status);
            if (data.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != data.EnumerateObject().Count())
                return ManagedMistakeResult.Unknown("invalid_response");
            if (!data.TryGetProperty("createdItemIds", out var ids) || ids.ValueKind != JsonValueKind.Array
                || ids.GetArrayLength() is < 1 or > 100) return ManagedMistakeResult.Unknown("invalid_response");
            var parsed = new List<Guid>();
            foreach (var id in ids.EnumerateArray())
            {
                if (id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var value)
                    || value == Guid.Empty || parsed.Contains(value)) return ManagedMistakeResult.Unknown("invalid_response");
                parsed.Add(value);
            }
            return new("Completed", "", parsed, status);
        }
        catch (OperationCanceledException) { return ManagedMistakeResult.Unknown(ct.IsCancellationRequested ? "cancelled" : "timeout_unknown"); }
        catch (HttpRequestException) { return ManagedMistakeResult.Unknown("transport_unknown"); }
        catch (IOException) { return ManagedMistakeResult.Unknown("transport_unknown"); }
        catch (JsonException) { return ManagedMistakeResult.Unknown("invalid_response"); }
    }
}
