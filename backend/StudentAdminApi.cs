using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Admin.WebApi.Models;
using Grpc.Core;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using System.Text.Json;

namespace Admin.WebApi;

internal static class StudentAdminApi
{
    private const string GroupPath = "/api/admin";

    private static readonly Dictionary<int, string> GradeLabels = new()
    {
        [1] = "小学一年级",
        [2] = "小学二年级",
        [3] = "小学三年级",
        [4] = "小学四年级",
        [5] = "小学五年级",
        [6] = "小学六年级",
        [7] = "初中一年级",
        [8] = "初中二年级",
        [9] = "初中三年级",
        [10] = "高中一年级",
        [11] = "高中二年级",
        [12] = "高中三年级",
    };

    public static void MapStudentAdminApi(this WebApplication app, int adminApiPort)
    {
        var group = app.MapGroup(GroupPath);

        var students = group.MapGroup("/students");

        students.MapGet("", ListStudentsAsync);
        students.MapGet("grades", GetGrades);
        students.MapGet("{studentId:guid}", GetStudentAsync);
        students.MapPost("", CreateStudentAsync);
        students.MapPut("{studentId:guid}", UpdateStudentAsync);
        students.MapDelete("{studentId:guid}", DeleteStudentAsync);

        students.MapGet("{studentId:guid}/accounts", GetIdentityAccountsByStudentIdAsync);
        students.MapPost("{studentId:guid}/accounts", LinkIdentityAccountToStudentAsync);
        students.MapDelete("{studentId:guid}/accounts/{accountId:guid}", UnlinkIdentityAccountFromStudentAsync);

        // 批量查询 Identity 用户信息接口
        group.MapPost("identity-accounts/batch", GetIdentityAccountsBatchAsync);

        group.MapGet("accounts/{accountId:guid}/students", GetStudentsByIdentityAccountIdAsync);
    }

    private static bool IsValidGuid(string value) => Guid.TryParse(value, out _);

    private static bool IsValidGrade(int grade) => GradeLabels.ContainsKey(grade);

    private static Ok<List<GradeOption>> GetGrades()
    {
        var grades = GradeLabels
            .Select(kv => new GradeOption(kv.Key, kv.Value))
            .OrderBy(g => g.Value)
            .ToList();
        return TypedResults.Ok(grades);
    }

    private static async Task<Results<Ok<PagedResponse<StudentDto>>, BadRequest<ErrorResponse>>> ListStudentsAsync(
        string? name,
        int? grade,
        int? page,
        int? pageSize,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient)
    {
        try
        {
            var normalizedPage = page.GetValueOrDefault(1) < 1 ? 1 : page.GetValueOrDefault(1);
            var normalizedPageSize = Math.Clamp(pageSize.GetValueOrDefault(20), 1, 100);

            var request = new SProto.ListStudentsRequest
            {
                Name = name ?? "",
                Grade = grade.HasValue && grade.Value > 0 ? (SProto.Grade)grade.Value : SProto.Grade.Unspecified,
                Page = normalizedPage,
                PageSize = normalizedPageSize
            };

            var response = await grpcClient.ListStudentsAsync(request).ConfigureAwait(false);

            var dtos = response.Items.Select(ToDto).ToList();
            return TypedResults.Ok(new PagedResponse<StudentDto>(dtos, response.TotalCount, response.Page, response.PageSize));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return TypedResults.BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    private static async Task<Results<Ok<StudentDto>, NotFound<ErrorResponse>, BadRequest<ErrorResponse>>> GetStudentAsync(
        Guid studentId,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient)
    {
        try
        {
            var request = new SProto.GetStudentRequest { StudentId = studentId.ToString() };
            var response = await grpcClient.GetStudentAsync(request).ConfigureAwait(false);
            return TypedResults.Ok(ToDto(response));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return TypedResults.NotFound(new ErrorResponse(ex.Status.Detail));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return TypedResults.BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    private static async Task<Results<Ok<StudentDto>, BadRequest<ErrorResponse>>> CreateStudentAsync(
        Models.CreateStudentRequest request,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return TypedResults.BadRequest(new ErrorResponse("Name is required."));

            if (!IsValidGrade(request.Grade))
                return TypedResults.BadRequest(new ErrorResponse("Invalid grade value."));

            if (request.IdentityAccountIds == null || request.IdentityAccountIds.Count == 0)
                return TypedResults.BadRequest(new ErrorResponse("At least one Identity Account ID is required."));

            var invalidIds = request.IdentityAccountIds.Where(id => !IsValidGuid(id)).ToList();
            if (invalidIds.Count > 0)
                return TypedResults.BadRequest(new ErrorResponse($"Invalid Identity Account ID format: {string.Join(", ", invalidIds)}"));

            var grpcRequest = new SProto.CreateStudentRequest
            {
                Name = request.Name.Trim(),
                Grade = (SProto.Grade)request.Grade,
                IdentityAccountIds = { request.IdentityAccountIds }
            };

            var response = await grpcClient.CreateStudentAsync(grpcRequest).ConfigureAwait(false);
            return TypedResults.Ok(ToDto(response));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return TypedResults.BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    private static async Task<Results<Ok<OperationResponse>, NotFound<ErrorResponse>, BadRequest<ErrorResponse>>> UpdateStudentAsync(
        Guid studentId,
        Models.UpdateStudentRequest request,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return TypedResults.BadRequest(new ErrorResponse("Name is required."));

            if (!IsValidGrade(request.Grade))
                return TypedResults.BadRequest(new ErrorResponse("Invalid grade value."));

            var grpcRequest = new SProto.UpdateStudentRequest
            {
                StudentId = studentId.ToString(),
                Name = request.Name.Trim(),
                Grade = (SProto.Grade)request.Grade
            };

            if (request.IdentityAccountIds != null)
            {
                var invalidIds = request.IdentityAccountIds.Where(id => !IsValidGuid(id)).ToList();
                if (invalidIds.Count > 0)
                    return TypedResults.BadRequest(new ErrorResponse($"Invalid Identity Account ID format: {string.Join(", ", invalidIds)}"));
                grpcRequest.IdentityAccountIds.AddRange(request.IdentityAccountIds);
            }

            var response = await grpcClient.UpdateStudentAsync(grpcRequest).ConfigureAwait(false);

            if (!response.Success)
                return TypedResults.NotFound(new ErrorResponse(response.ErrorMessage));

            return TypedResults.Ok(new OperationResponse(true, "Student updated successfully."));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return TypedResults.BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    private static async Task<Results<Ok<OperationResponse>, NotFound<ErrorResponse>, BadRequest<ErrorResponse>>> DeleteStudentAsync(
        Guid studentId,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient)
    {
        try
        {
            var request = new SProto.DeleteStudentRequest { StudentId = studentId.ToString() };
            var response = await grpcClient.DeleteStudentAsync(request).ConfigureAwait(false);

            if (!response.Success)
                return TypedResults.NotFound(new ErrorResponse(response.ErrorMessage));

            return TypedResults.Ok(new OperationResponse(true, "Student deleted."));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return TypedResults.BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    private static async Task<Ok<IReadOnlyList<string>>> GetIdentityAccountsByStudentIdAsync(
        Guid studentId,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient)
    {
        var request = new SProto.GetAccountsByStudentIdRequest { StudentId = studentId.ToString() };
        var response = await grpcClient.GetIdentityAccountsByStudentIdAsync(request).ConfigureAwait(false);
        return TypedResults.Ok((IReadOnlyList<string>)response.AccountIds.ToList());
    }

    private static async Task<Results<Ok<OperationResponse>, NotFound<ErrorResponse>, BadRequest<ErrorResponse>>> LinkIdentityAccountToStudentAsync(
        Guid studentId,
        LinkUserRequest request,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.IdentityAccountId))
                return TypedResults.BadRequest(new ErrorResponse("Identity Account ID is required."));

            if (!IsValidGuid(request.IdentityAccountId))
                return TypedResults.BadRequest(new ErrorResponse("Invalid Identity Account ID format. Must be a valid GUID."));

            var grpcRequest = new SProto.LinkAccountRequest
            {
                StudentId = studentId.ToString(),
                IdentityAccountId = request.IdentityAccountId
            };

            var response = await grpcClient.LinkIdentityAccountToStudentAsync(grpcRequest).ConfigureAwait(false);

            if (!response.Success)
                return TypedResults.NotFound(new ErrorResponse(response.ErrorMessage));

            return TypedResults.Ok(new OperationResponse(true, "Identity account linked to student successfully."));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return TypedResults.BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    private static async Task<Results<Ok<OperationResponse>, NotFound<ErrorResponse>, BadRequest<ErrorResponse>>> UnlinkIdentityAccountFromStudentAsync(
        Guid studentId,
        Guid accountId,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient)
    {
        try
        {
            var request = new SProto.UnlinkAccountRequest
            {
                StudentId = studentId.ToString(),
                AccountId = accountId.ToString()
            };

            var response = await grpcClient.UnlinkIdentityAccountFromStudentAsync(request).ConfigureAwait(false);

            if (!response.Success)
                return TypedResults.NotFound(new ErrorResponse(response.ErrorMessage));

            return TypedResults.Ok(new OperationResponse(true, "Identity account unlinked from student."));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return TypedResults.BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    private static async Task<Ok<IReadOnlyList<StudentDto>>> GetStudentsByIdentityAccountIdAsync(
        Guid accountId,
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient)
    {
        var request = new SProto.GetStudentsByAccountIdRequest { AccountId = accountId.ToString() };
        var response = await grpcClient.GetStudentsByIdentityAccountIdAsync(request).ConfigureAwait(false);
        var dtos = response.Students.Select(ToDto).ToList();
        return TypedResults.Ok((IReadOnlyList<StudentDto>)dtos);
    }

    private static StudentDto ToDto(SProto.StudentDto model) => new(
        model.Id,
        model.Name,
        (int)model.Grade,
        model.IdentityAccountIds.ToList(),
        model.CreatedAt,
        model.UpdatedAt);

    // 批量查询 Identity 用户信息 DTO
    public sealed record IdentityAccountDto(string UserId, string Username, string DisplayName, string Phone, string Remark);

    // Identity 服务返回的用户列表项
    private sealed record IdentityUserItem(string UserId, string Username, string Phone, string Remark, string DisplayName);

    // 批量查询 Identity 用户信息
    // 通过 admin_portal 自身的 proxy 路径 /api/identity/admin/users 间接调用 Identity 服务
    private static async Task<Ok<List<IdentityAccountDto>>> GetIdentityAccountsBatchAsync(
        HttpContext httpContext,
        [FromBody] List<string> accountIds)
    {
        if (accountIds == null || accountIds.Count == 0)
            return TypedResults.Ok(new List<IdentityAccountDto>());

        var targetIds = new HashSet<string>(accountIds, StringComparer.OrdinalIgnoreCase);
        var result = new List<IdentityAccountDto>();

        try
        {
            var appId = httpContext.Request.Headers["X-Admin-AppId"].FirstOrDefault();
            var appSecret = httpContext.Request.Headers["X-Admin-AppSecret"].FirstOrDefault();

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appSecret))
                return TypedResults.Ok(result);

            // 通过 admin_portal 自身地址调用 /api/identity/admin/users，该请求会经过 proxy 中间件
            // 避免了直接调用 Identity 服务时的 cookie 认证问题
            var scheme = httpContext.Request.Scheme;
            var host = httpContext.Request.Host.Value;
            var baseUrl = $"{scheme}://{host}";

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            int page = 1;
            const int pageSize = 100;
            bool hasMore = true;

            while (hasMore)
            {
                var url = $"{baseUrl}/api/identity/admin/users?page={page}&pageSize={pageSize}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-Admin-AppId", appId);
                request.Headers.Add("X-Admin-AppSecret", appSecret);

                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    break;

                var json = await response.Content.ReadAsStringAsync();
                var pagedResult = JsonSerializer.Deserialize<IdentityPagedResult<IdentityUserItem>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (pagedResult?.Items == null || pagedResult.Items.Count == 0)
                    break;

                foreach (var user in pagedResult.Items)
                {
                    if (targetIds.Contains(user.UserId))
                    {
                        result.Add(new IdentityAccountDto(
                            user.UserId,
                            user.Username ?? string.Empty,
                            user.DisplayName ?? string.Empty,
                            user.Phone ?? string.Empty,
                            user.Remark ?? string.Empty));
                    }
                }

                hasMore = pagedResult.Items.Count == pageSize;
                page++;
            }
        }
        catch (Exception ex)
        {
            // 记录错误
            var loggerFactory = httpContext.RequestServices.GetService<ILoggerFactory>();
            loggerFactory?.CreateLogger("StudentAdminApi").LogWarning(ex, "Failed to batch-query identity accounts");
        }

        return TypedResults.Ok(result);
    }

    // JSON 反序列化辅助类型
    private sealed record IdentityPagedResult<T>(List<T> Items, int Total, int Page, int PageSize);
}
