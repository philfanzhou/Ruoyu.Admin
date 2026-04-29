using Microsoft.AspNetCore.Http.HttpResults;
using Admin.WebApi.Models;
using Grpc.Core;
using SProto = Ruoyu.Study.Student.Contract.Protos;

namespace Admin.WebApi;

internal static class StudentAdminApi
{
    private const string GroupPath = "/api/admin";
    private const string AppIdHeader = "X-Admin-AppId";
    private const string AppSecretHeader = "X-Admin-AppSecret";

    public static void MapStudentAdminApi(this WebApplication app, int adminApiPort)
    {
        var group = app.MapGroup(GroupPath);

        group.AddEndpointFilterFactory((_, next) => invocationContext =>
            ValidateRequestAsync(invocationContext, next, adminApiPort));

        var students = group.MapGroup("/students");

        students.MapGet("", ListStudentsAsync);
        students.MapGet("{studentId:guid}", GetStudentAsync);
        students.MapPost("", CreateStudentAsync);
        students.MapPut("{studentId:guid}", UpdateStudentAsync);
        students.MapDelete("{studentId:guid}", DeleteStudentAsync);

        students.MapGet("{studentId:guid}/accounts", GetIdentityAccountsByStudentIdAsync);
        students.MapPost("{studentId:guid}/accounts", LinkIdentityAccountToStudentAsync);
        students.MapDelete("{studentId:guid}/accounts/{accountId:guid}", UnlinkIdentityAccountFromStudentAsync);

        group.MapGet("accounts/{accountId:guid}/students", GetStudentsByIdentityAccountIdAsync);
    }

    private static async ValueTask<object?> ValidateRequestAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next,
        int adminApiPort)
    {
        var httpContext = context.HttpContext;
        if (httpContext.Connection.LocalPort != adminApiPort)
            return TypedResults.NotFound();

        var appId = httpContext.Request.Headers[AppIdHeader].ToString();
        var appSecret = httpContext.Request.Headers[AppSecretHeader].ToString();
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
            return TypedResults.Unauthorized();

        var credentials = httpContext.RequestServices.GetRequiredService<AdminCredentials>();
        if (appId != credentials.AppId || !BCrypt.Net.BCrypt.Verify(appSecret, credentials.AppSecret))
            return TypedResults.Json(new ErrorResponse("Invalid admin credentials"), statusCode: StatusCodes.Status401Unauthorized);

        return await next(context);
    }

    private static bool IsValidGuid(string value) => Guid.TryParse(value, out _);

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
                Grade = grade.GetValueOrDefault(),
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

            if (request.IdentityAccountIds == null || request.IdentityAccountIds.Count == 0)
                return TypedResults.BadRequest(new ErrorResponse("At least one Identity Account ID is required."));

            var invalidIds = request.IdentityAccountIds.Where(id => !IsValidGuid(id)).ToList();
            if (invalidIds.Count > 0)
                return TypedResults.BadRequest(new ErrorResponse($"Invalid Identity Account ID format: {string.Join(", ", invalidIds)}"));

            var grpcRequest = new SProto.CreateStudentRequest
            {
                Name = request.Name.Trim(),
                Grade = request.Grade,
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

            var grpcRequest = new SProto.UpdateStudentRequest
            {
                StudentId = studentId.ToString(),
                Name = request.Name.Trim(),
                Grade = request.Grade
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
        model.Grade,
        model.IdentityAccountIds.ToList(),
        model.CreatedAt,
        model.UpdatedAt);
}
