using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text.Json;
using Ruoyu.Study.MistakeBff.HttpClients;
using Admin.WebApi.Models;

namespace Admin.WebApi.Controllers;

[Route("api/admin/students")]
[ApiController]
[Authorize]
public class StudentAssociationsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IStudentHttpClient _studentClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StudentAssociationsController> _logger;

    public StudentAssociationsController(
        IHttpClientFactory httpClientFactory,
        IStudentHttpClient studentClient,
        IConfiguration configuration,
        ILogger<StudentAssociationsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _studentClient = studentClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 获取学生关联的教师和助教列表。
    /// 先获取学生的 identityAccountIds，再反查 TeacherPortal 和 AssistantPortal。
    /// </summary>
    [HttpGet("{studentId:guid}/linked-accounts")]
    public async Task<IActionResult> GetLinkedAccounts(Guid studentId)
    {
        List<string> accountIds;
        try
        {
            accountIds = await _studentClient.GetIdentityAccountsByStudentIdAsync(studentId.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get identity accounts for student {StudentId}", studentId);
            return Ok(new LinkedAccountsResponse(Array.Empty<LinkedAccountDto>(), Array.Empty<LinkedAccountDto>()));
        }

        if (accountIds.Count == 0)
        {
            return Ok(new LinkedAccountsResponse(Array.Empty<LinkedAccountDto>(), Array.Empty<LinkedAccountDto>()));
        }

        var accountIdSet = new HashSet<string>(accountIds, StringComparer.OrdinalIgnoreCase);

        var teacherPortalUrl = _configuration["TeacherPortal:Url"];
        var assistantPortalUrl = _configuration["AssistantPortal:Url"];

        var teachers = new List<LinkedAccountDto>();
        var assistants = new List<LinkedAccountDto>();

        if (!string.IsNullOrWhiteSpace(teacherPortalUrl))
        {
            teachers = await FetchLinkedAccountsAsync("TeacherPortal", $"{teacherPortalUrl.TrimEnd('/')}/api/admin/teachers", "教师", accountIdSet);
        }

        if (!string.IsNullOrWhiteSpace(assistantPortalUrl))
        {
            assistants = await FetchLinkedAccountsAsync("AssistantPortal", $"{assistantPortalUrl.TrimEnd('/')}/api/admin/assistants", "助教", accountIdSet);
        }

        return Ok(new LinkedAccountsResponse(teachers, assistants));
    }

    private async Task<List<LinkedAccountDto>> FetchLinkedAccountsAsync(string clientName, string url, string roleLabel, HashSet<string> accountIds)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(clientName);

            // Forward the caller's JWT to the downstream portal. The TeacherPortal/AssistantPortal
            // /api/admin/{role}s endpoints require [Authorize(Roles="admin")]; without the
            // Authorization header the call returns 401 and the aggregate query silently degrades
            // to an empty list (regression fixed on 2026-07-27).
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var authHeader = HttpContext.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrEmpty(authHeader))
            {
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(authHeader);
            }

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch {Role} list: {StatusCode}", roleLabel, response.StatusCode);
                return new List<LinkedAccountDto>();
            }

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = doc.RootElement;

            var dataElement = root.ValueKind == JsonValueKind.Array
                ? root
                : (root.TryGetProperty("data", out var d) ? d : root);

            if (dataElement.ValueKind != JsonValueKind.Array)
            {
                return new List<LinkedAccountDto>();
            }

            var result = new List<LinkedAccountDto>();
            foreach (var item in dataElement.EnumerateArray())
            {
                var userId = GetStringProp(item, "userId", "UserId");
                if (string.IsNullOrWhiteSpace(userId) || !accountIds.Contains(userId))
                {
                    continue;
                }

                var username = GetStringProp(item, "username", "Username");
                var phone = GetStringProp(item, "phone", "Phone");
                var displayName = username ?? phone ?? userId[..Math.Min(8, userId.Length)];
                var subjects = GetSubjectsString(item);

                result.Add(new LinkedAccountDto(userId, displayName ?? string.Empty, phone ?? string.Empty, subjects, roleLabel));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch {Role} list from {Url}", roleLabel, url);
            return new List<LinkedAccountDto>();
        }
    }

    private static string? GetStringProp(JsonElement element, string camelCase, string pascalCase)
    {
        if (element.TryGetProperty(camelCase, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        if (element.TryGetProperty(pascalCase, out prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static string GetSubjectsString(JsonElement element)
    {
        foreach (var name in new[] { "subjects", "Subjects" })
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                var items = new List<string>();
                foreach (var item in prop.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number)
                    {
                        items.Add(item.GetInt32().ToString());
                    }
                    else if (item.ValueKind == JsonValueKind.String)
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrEmpty(s))
                        {
                            items.Add(s);
                        }
                    }
                }
                return string.Join(",", items);
            }
        }
        return string.Empty;
    }
}

public sealed record LinkedAccountDto(
    string UserId,
    string DisplayName,
    string Phone,
    string Subjects,
    string Role);

public sealed record LinkedAccountsResponse(
    IReadOnlyList<LinkedAccountDto> Teachers,
    IReadOnlyList<LinkedAccountDto> Assistants);
