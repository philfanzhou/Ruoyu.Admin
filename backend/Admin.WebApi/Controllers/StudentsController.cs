using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruoyu.Admin.Common.Constants;
using Ruoyu.Admin.ServiceClients;
using Admin.WebApi.Models;
using StudentHttpDto = Ruoyu.Admin.ServiceClients.StudentDto;

namespace Admin.WebApi.Controllers;

[Route("api/admin/students")]
[ApiController, RequireSecurityResponseHeaders]
[Authorize]
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

    private readonly IStudentHttpClient _studentClient;

    public StudentsController(IStudentHttpClient studentClient)
    {
        _studentClient = studentClient;
    }

    // Downstream failures are no longer relayed as 400/404 with the downstream message: the
    // exceptions propagate to the ServiceMantle Problem Details boundary, which answers 502
    // problem+json downstream.unavailable (fixed title/code, correlation id, no exception text).

    [HttpGet]
    public async Task<IActionResult> ListStudents(
        [FromQuery] string? name,
        [FromQuery] int? grade,
        [FromQuery] int? page,
        [FromQuery] int? pageSize)
    {
        var normalizedPage = page.GetValueOrDefault(1) < 1 ? 1 : page.GetValueOrDefault(1);
        var normalizedPageSize = Math.Clamp(pageSize.GetValueOrDefault(20), 1, 100);

        var response = await _studentClient.ListStudentsAsync(grade, normalizedPage, normalizedPageSize, name);
        var dtos = response.Items.Select(ToDto).ToList();
        return Ok(new PagedResponse<Models.StudentDto>(dtos, response.TotalCount, response.Page, response.PageSize));
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
        var response = await _studentClient.GetStudentAsync(studentId.ToString());
        return Ok(ToDto(response));
    }

    [HttpPost]
    public async Task<IActionResult> CreateStudent([FromBody] CreateStudentRequest request)
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

        var response = await _studentClient.CreateStudentAsync(request.Name.Trim(), request.Grade, request.IdentityAccountIds);
        return Ok(ToDto(response));
    }

    [HttpPut("{studentId:guid}")]
    public async Task<IActionResult> UpdateStudent(Guid studentId, [FromBody] UpdateStudentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new ErrorResponse("Name is required."));

        if (!IsValidGrade(request.Grade))
            return BadRequest(new ErrorResponse("Invalid grade value."));

        if (request.IdentityAccountIds != null)
        {
            var invalidIds = request.IdentityAccountIds.Where(id => !IsValidGuid(id)).ToList();
            if (invalidIds.Count > 0)
                return BadRequest(new ErrorResponse($"Invalid Identity Account ID format: {string.Join(", ", invalidIds)}"));
        }

        await _studentClient.UpdateStudentAsync(studentId.ToString(), request.Name.Trim(), request.Grade, request.IdentityAccountIds);

        return Ok(new OperationResponse(true, "Student updated successfully."));
    }

    [HttpDelete("{studentId:guid}")]
    public async Task<IActionResult> DeleteStudent(Guid studentId)
    {
        await _studentClient.DeleteStudentAsync(studentId.ToString());
        return Ok(new OperationResponse(true, "Student deleted."));
    }

    [HttpGet("{studentId:guid}/accounts")]
    public async Task<IActionResult> GetIdentityAccountsByStudentId(Guid studentId)
    {
        var accountIds = await _studentClient.GetIdentityAccountsByStudentIdAsync(studentId.ToString());
        return Ok((IReadOnlyList<string>)accountIds);
    }

    [HttpPost("{studentId:guid}/accounts")]
    public async Task<IActionResult> LinkIdentityAccountToStudent(Guid studentId, [FromBody] LinkUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdentityAccountId))
            return BadRequest(new ErrorResponse("Identity Account ID is required."));

        if (!IsValidGuid(request.IdentityAccountId))
            return BadRequest(new ErrorResponse("Invalid Identity Account ID format. Must be a valid GUID."));

        await _studentClient.LinkIdentityAccountToStudentAsync(studentId.ToString(), request.IdentityAccountId);

        return Ok(new OperationResponse(true, "Identity account linked to student successfully."));
    }

    [HttpDelete("{studentId:guid}/accounts/{accountId:guid}")]
    public async Task<IActionResult> UnlinkIdentityAccountFromStudent(Guid studentId, Guid accountId)
    {
        await _studentClient.UnlinkIdentityAccountFromStudentAsync(studentId.ToString(), accountId.ToString());

        return Ok(new OperationResponse(true, "Identity account unlinked from student."));
    }

    [HttpGet("{studentId:guid}/open-subjects")]
    public async Task<IActionResult> GetStudentOpenSubjects(Guid studentId, [FromQuery] bool? activeOnly)
    {
        var response = await _studentClient.GetStudentOpenSubjectsAsync(studentId.ToString(), activeOnly ?? false);

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
        var items = new List<SetOpenSubjectItem>();

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

                items.Add(new SetOpenSubjectItem
                {
                    Subject = subject.Subject,
                    OpenStartDate = subject.OpenStartDate,
                    OpenEndDate = endDate?.ToString(SubjectConstants.DateFormat)
                });
            }
        }

        await _studentClient.SetStudentOpenSubjectsAsync(studentId.ToString(), items);

        return Ok(new OperationResponse(true, "Open subjects updated successfully."));
    }

    [HttpGet("subject-options")]
    public async Task<IActionResult> GetSubjectOptions()
    {
        var response = await _studentClient.GetAvailableSubjectsAsync();

        var options = response.Subjects.Select(s => new SubjectOption(s.Value, s.Name, s.DisplayName)).ToList();

        return Ok(options);
    }

    private static Models.StudentDto ToDto(StudentHttpDto model) => new(
        model.Id,
        model.Name,
        model.Grade,
        model.IdentityAccountIds.ToList(),
        model.CreatedAt,
        model.UpdatedAt);

    private static bool IsValidGuid(string value) => Guid.TryParse(value, out _);

    private static bool IsValidGrade(int grade) => GradeLabels.ContainsKey(grade);
}
