using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Admin.WebApi.Models;

namespace Admin.WebApi.Controllers;

[Route("api/admin")]
[ApiController]
public class IdentityAccountsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IdentityServiceOptions _options;
    private readonly ILogger<IdentityAccountsController> _logger;

    public IdentityAccountsController(
        IHttpClientFactory httpClientFactory,
        IOptions<IdentityServiceOptions> options,
        ILogger<IdentityAccountsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost("identity-accounts/batch")]
    public async Task<IActionResult> GetIdentityAccountsBatch([FromBody] List<string> accountIds)
    {
        if (accountIds == null || accountIds.Count == 0)
            return Ok(new { accounts = (IReadOnlyList<IdentityAccountDto>)new List<IdentityAccountDto>(), partialFailure = false, warning = (string?)null });

        var targetIds = new HashSet<string>(accountIds, StringComparer.OrdinalIgnoreCase);
        var result = new List<IdentityAccountDto>();
        var partialFailure = false;

        try
        {
            if (string.IsNullOrEmpty(_options.AppId) || string.IsNullOrEmpty(_options.AppSecret))
                return Ok(new { accounts = (IReadOnlyList<IdentityAccountDto>)result, partialFailure, warning = (string?)null });

            var client = _httpClientFactory.CreateClient("IdentityService");
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.Address.TrimEnd('/')}/api/gateway/users/batch");
            request.Headers.Add("X-Admin-AppId", _options.AppId);
            request.Headers.Add("X-Admin-AppSecret", _options.AppSecret);
            request.Content = JsonContent.Create(accountIds);

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return Ok(new { accounts = (IReadOnlyList<IdentityAccountDto>)result, partialFailure, warning = (string?)null });

            var accounts = await response.Content.ReadFromJsonAsync<List<IdentityUserItem>>();
            if (accounts == null)
                return Ok(new { accounts = (IReadOnlyList<IdentityAccountDto>)result, partialFailure, warning = (string?)null });

            foreach (var user in accounts)
            {
                if (!targetIds.Contains(user.UserId))
                    continue;

                result.Add(new IdentityAccountDto(
                    user.UserId,
                    user.Username ?? string.Empty,
                    user.DisplayName ?? string.Empty,
                    user.Phone ?? string.Empty,
                    user.Remark ?? string.Empty));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to batch-query identity accounts");
            partialFailure = true;
        }

        return Ok(new { accounts = (IReadOnlyList<IdentityAccountDto>)result, partialFailure, warning = partialFailure ? "Identity 服务暂时不可用，部分账户信息无法加载" : (string?)null });
    }

    [HttpGet("accounts/{accountId:guid}/students")]
    public async Task<IActionResult> GetStudentsByIdentityAccountId(
        Guid accountId,
        Ruoyu.Study.Student.Contract.Protos.StudentLearningGrpcService.StudentLearningGrpcServiceClient grpcClient)
    {
        var request = new Ruoyu.Study.Student.Contract.Protos.GetStudentsByAccountIdRequest
        {
            AccountId = accountId.ToString()
        };
        var response = await grpcClient.GetStudentsByIdentityAccountIdAsync(request);
        var dtos = response.Students.Select(s => new StudentDto(
            s.Id,
            s.Name,
            (int)s.Grade,
            s.IdentityAccountIds.ToList(),
            s.CreatedAt,
            s.UpdatedAt)).ToList();
        return Ok((IReadOnlyList<StudentDto>)dtos);
    }
}
