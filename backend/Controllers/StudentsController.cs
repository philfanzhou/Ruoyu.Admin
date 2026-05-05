using Microsoft.AspNetCore.Mvc;
using Grpc.Core;
using Ruoyu.Study.Common.Constants;
using Admin.WebApi.Models;
using GrpcStatusCode = Grpc.Core.StatusCode;
using SProto = Ruoyu.Study.Student.Contract.Protos;

namespace Admin.WebApi.Controllers;

[Route("api/admin/students")]
[ApiController]
public class StudentsController : ControllerBase
{
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

    private readonly SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient _grpcClient;
    private readonly ILogger<StudentsController> _logger;

    public StudentsController(
        SProto.StudentManagementGrpcService.StudentManagementGrpcServiceClient grpcClient,
        ILogger<StudentsController> logger)
    {
        _grpcClient = grpcClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> ListStudents(
        [FromQuery] string? name,
        [FromQuery] int? grade,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
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

            var response = await _grpcClient.ListStudentsAsync(request);
            var dtos = response.Items.Select(ToDto).ToList();
            return Ok(new PagedResponse<StudentDto>(dtos, response.TotalCount, response.Page, response.PageSize));
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.InvalidArgument)
        {
            return BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    [HttpGet("grades")]
    public IActionResult GetGrades()
    {
        var grades = GradeLabels
            .Select(kv => new GradeOption(kv.Key, kv.Value))
            .OrderBy(g => g.Value)
            .ToList();
        return Ok(grades);
    }

    [HttpGet("{studentId:guid}")]
    public async Task<IActionResult> GetStudent(Guid studentId)
    {
        try
        {
            var request = new SProto.GetStudentRequest { StudentId = studentId.ToString() };
            var response = await _grpcClient.GetStudentAsync(request);
            return Ok(ToDto(response));
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.NotFound)
        {
            return NotFound(new ErrorResponse(ex.Status.Detail));
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.InvalidArgument)
        {
            return BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateStudent([FromBody] CreateStudentRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new ErrorResponse("Name is required."));

            if (!IsValidGrade(request.Grade))
                return BadRequest(new ErrorResponse("Invalid grade value."));

            if (request.IdentityAccountIds == null || request.IdentityAccountIds.Count == 0)
                return BadRequest(new ErrorResponse("At least one Identity Account ID is required."));

            var invalidIds = request.IdentityAccountIds.Where(id => !IsValidGuid(id)).ToList();
            if (invalidIds.Count > 0)
                return BadRequest(new ErrorResponse($"Invalid Identity Account ID format: {string.Join(", ", invalidIds)}"));

            var grpcRequest = new SProto.CreateStudentRequest
            {
                Name = request.Name.Trim(),
                Grade = (SProto.Grade)request.Grade,
                IdentityAccountIds = { request.IdentityAccountIds }
            };

            var response = await _grpcClient.CreateStudentAsync(grpcRequest);
            return Ok(ToDto(response));
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.InvalidArgument)
        {
            return BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    [HttpPut("{studentId:guid}")]
    public async Task<IActionResult> UpdateStudent(Guid studentId, [FromBody] UpdateStudentRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new ErrorResponse("Name is required."));

            if (!IsValidGrade(request.Grade))
                return BadRequest(new ErrorResponse("Invalid grade value."));

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
                    return BadRequest(new ErrorResponse($"Invalid Identity Account ID format: {string.Join(", ", invalidIds)}"));
                grpcRequest.IdentityAccountIds.AddRange(request.IdentityAccountIds);
            }

            var response = await _grpcClient.UpdateStudentAsync(grpcRequest);

            if (!response.Success)
                return NotFound(new ErrorResponse(response.ErrorMessage));

            return Ok(new OperationResponse(true, "Student updated successfully."));
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.InvalidArgument)
        {
            return BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    [HttpDelete("{studentId:guid}")]
    public async Task<IActionResult> DeleteStudent(Guid studentId)
    {
        try
        {
            var request = new SProto.DeleteStudentRequest { StudentId = studentId.ToString() };
            var response = await _grpcClient.DeleteStudentAsync(request);

            if (!response.Success)
                return NotFound(new ErrorResponse(response.ErrorMessage));

            return Ok(new OperationResponse(true, "Student deleted."));
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.InvalidArgument)
        {
            return BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    [HttpGet("{studentId:guid}/accounts")]
    public async Task<IActionResult> GetIdentityAccountsByStudentId(Guid studentId)
    {
        var request = new SProto.GetAccountsByStudentIdRequest { StudentId = studentId.ToString() };
        var response = await _grpcClient.GetIdentityAccountsByStudentIdAsync(request);
        return Ok((IReadOnlyList<string>)response.AccountIds.ToList());
    }

    [HttpPost("{studentId:guid}/accounts")]
    public async Task<IActionResult> LinkIdentityAccountToStudent(Guid studentId, [FromBody] LinkUserRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.IdentityAccountId))
                return BadRequest(new ErrorResponse("Identity Account ID is required."));

            if (!IsValidGuid(request.IdentityAccountId))
                return BadRequest(new ErrorResponse("Invalid Identity Account ID format. Must be a valid GUID."));

            var grpcRequest = new SProto.LinkAccountRequest
            {
                StudentId = studentId.ToString(),
                IdentityAccountId = request.IdentityAccountId
            };

            var response = await _grpcClient.LinkIdentityAccountToStudentAsync(grpcRequest);

            if (!response.Success)
                return NotFound(new ErrorResponse(response.ErrorMessage));

            return Ok(new OperationResponse(true, "Identity account linked to student successfully."));
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.InvalidArgument)
        {
            return BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    [HttpDelete("{studentId:guid}/accounts/{accountId:guid}")]
    public async Task<IActionResult> UnlinkIdentityAccountFromStudent(Guid studentId, Guid accountId)
    {
        try
        {
            var request = new SProto.UnlinkAccountRequest
            {
                StudentId = studentId.ToString(),
                AccountId = accountId.ToString()
            };

            var response = await _grpcClient.UnlinkIdentityAccountFromStudentAsync(request);

            if (!response.Success)
                return NotFound(new ErrorResponse(response.ErrorMessage));

            return Ok(new OperationResponse(true, "Identity account unlinked from student."));
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.InvalidArgument)
        {
            return BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    [HttpGet("{studentId:guid}/open-subjects")]
    public async Task<IActionResult> GetStudentOpenSubjects(Guid studentId, [FromQuery] bool? activeOnly)
    {
        var request = new SProto.GetStudentOpenSubjectsRequest
        {
            StudentId = studentId.ToString(),
            ActiveOnly = activeOnly ?? false
        };
        var response = await _grpcClient.GetStudentOpenSubjectsAsync(request);

        var dtos = response.Subjects.Select(s => new OpenSubjectDto(
            s.Id,
            s.Subject,
            s.OpenStartDate,
            string.IsNullOrEmpty(s.OpenEndDate) ? null : s.OpenEndDate,
            s.IsActive)).ToList();

        return Ok(dtos);
    }

    [HttpPut("{studentId:guid}/open-subjects")]
    public async Task<IActionResult> SetStudentOpenSubjects(Guid studentId, [FromBody] SetOpenSubjectsRequest request)
    {
        try
        {
            var grpcRequest = new SProto.SetOpenSubjectsRequest
            {
                StudentId = studentId.ToString()
            };

            if (request.Subjects != null && request.Subjects.Count > 0)
            {
                foreach (var subject in request.Subjects)
                {
                    if (!SubjectConstants.IsValid(subject.Subject))
                        return BadRequest(new ErrorResponse($"Invalid subject value: {subject.Subject}"));

                    if (string.IsNullOrEmpty(subject.OpenStartDate))
                        return BadRequest(new ErrorResponse("Open start date is required"));

                    if (!DateOnly.TryParse(subject.OpenStartDate, out _))
                        return BadRequest(new ErrorResponse($"Invalid start date format: {subject.OpenStartDate}"));

                    DateOnly? endDate = null;
                    if (!string.IsNullOrEmpty(subject.OpenEndDate))
                    {
                        if (!DateOnly.TryParse(subject.OpenEndDate, out var parsed))
                            return BadRequest(new ErrorResponse($"Invalid end date format: {subject.OpenEndDate}"));
                        endDate = parsed;
                    }

                    grpcRequest.Subjects.Add(new SProto.OpenSubjectItem
                    {
                        Subject = subject.Subject,
                        OpenStartDate = subject.OpenStartDate,
                        OpenEndDate = endDate?.ToString(SubjectConstants.DateFormat) ?? ""
                    });
                }
            }

            var response = await _grpcClient.SetStudentOpenSubjectsAsync(grpcRequest);

            if (!response.Success)
                return BadRequest(new ErrorResponse(response.ErrorMessage));

            return Ok(new OperationResponse(true, "Open subjects updated successfully."));
        }
        catch (RpcException ex) when (ex.StatusCode == GrpcStatusCode.InvalidArgument)
        {
            return BadRequest(new ErrorResponse(ex.Status.Detail));
        }
    }

    [HttpGet("subject-options")]
    public async Task<IActionResult> GetSubjectOptions()
    {
        var request = new SProto.Empty();
        var response = await _grpcClient.GetAvailableSubjectsAsync(request);

        var options = response.Subjects.Select(s => new SubjectOption(s.Value, s.Name, s.DisplayName)).ToList();
        return Ok(options);
    }

    private static StudentDto ToDto(SProto.StudentDto model) => new(
        model.Id,
        model.Name,
        (int)model.Grade,
        model.IdentityAccountIds.ToList(),
        model.CreatedAt,
        model.UpdatedAt);

    private static bool IsValidGuid(string value) => Guid.TryParse(value, out _);

    private static bool IsValidGrade(int grade) => GradeLabels.ContainsKey(grade);
}
