using System.Net;
using Admin.WebApi.Controllers;
using Admin.WebApi.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Study.MistakeBff.HttpClients;
using StudentHttpDto = Ruoyu.Study.MistakeBff.HttpClients.StudentDto;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// StudentsController UT：覆盖 ListStudents、CreateStudent、GetStudent。
/// </summary>
public class StudentsControllerTests
{
    private readonly Mock<IStudentHttpClient> _studentClient;
    private readonly Mock<ILogger<StudentsController>> _logger;
    private readonly StudentsController _controller;

    public StudentsControllerTests()
    {
        _studentClient = new Mock<IStudentHttpClient>();
        _logger = new Mock<ILogger<StudentsController>>();

        _controller = new StudentsController(
            _studentClient.Object,
            _logger.Object
        );
    }

    // ============================================================
    // ListStudents
    // ============================================================

    [Fact]
    public async Task ListStudents_ReturnsStudents()
    {
        var studentDto1 = new StudentHttpDto
        {
            Id = "s1",
            Name = "张三",
            Grade = 3,
            CreatedAt = 1700000000,
            UpdatedAt = 1700000001,
            IdentityAccountIds = new List<string> { "acc1" }
        };

        var studentDto2 = new StudentHttpDto
        {
            Id = "s2",
            Name = "李四",
            Grade = 5,
            CreatedAt = 1700000002,
            UpdatedAt = 1700000003,
            IdentityAccountIds = new List<string> { "acc2" }
        };

        var response = new PagedStudentsResult
        {
            TotalCount = 2,
            Page = 1,
            PageSize = 20,
            Items = new List<StudentHttpDto> { studentDto1, studentDto2 }
        };

        _studentClient
            .Setup(c => c.ListStudentsAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.ListStudents(null, null, null, null);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        // PagedResponse<StudentDto> has Items property
        var items = (IReadOnlyList<Admin.WebApi.Models.StudentDto>)typed.GetType().GetProperty("Items")!.GetValue(typed)!;
        items.Should().HaveCount(2);
        items[0].Name.Should().Be("张三");
        items[1].Name.Should().Be("李四");
    }

    [Fact]
    public async Task ListStudents_InvalidArgument_ReturnsBadRequest()
    {
        _studentClient
            .Setup(c => c.ListStudentsAsync(It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Invalid page", null, HttpStatusCode.BadRequest));

        var result = await _controller.ListStudents(null, null, null, null);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ============================================================
    // CreateStudent
    // ============================================================

    [Fact]
    public async Task CreateStudent_ValidRequest_ReturnsCreatedStudent()
    {
        var createdStudent = new StudentHttpDto
        {
            Id = Guid.NewGuid().ToString(),
            Name = "王五",
            Grade = 3,
            CreatedAt = 1700000000,
            UpdatedAt = 1700000001,
            IdentityAccountIds = new List<string> { "acc1" }
        };

        _studentClient
            .Setup(c => c.CreateStudentAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdStudent);

        var request = new CreateStudentRequest("王五", 3, new List<string> { Guid.NewGuid().ToString() });

        var result = await _controller.CreateStudent(request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.Should().BeOfType<Admin.WebApi.Models.StudentDto>();
        ((Admin.WebApi.Models.StudentDto)typed).Name.Should().Be("王五");
    }

    [Fact]
    public async Task CreateStudent_EmptyName_ReturnsBadRequest()
    {
        var request = new CreateStudentRequest("", 3, new List<string> { Guid.NewGuid().ToString() });

        var result = await _controller.CreateStudent(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateStudent_InvalidGrade_ReturnsBadRequest()
    {
        var request = new CreateStudentRequest("王五", 99, new List<string> { Guid.NewGuid().ToString() });

        var result = await _controller.CreateStudent(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateStudent_NoIdentityAccountIds_ReturnsBadRequest()
    {
        var request = new CreateStudentRequest("王五", 3, new List<string>());

        var result = await _controller.CreateStudent(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateStudent_InvalidIdentityAccountIdFormat_ReturnsBadRequest()
    {
        var request = new CreateStudentRequest("王五", 3, new List<string> { "not-a-guid" });

        var result = await _controller.CreateStudent(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ============================================================
    // GetStudent
    // ============================================================

    [Fact]
    public async Task GetStudent_ExistingStudent_ReturnsStudent()
    {
        var studentId = Guid.NewGuid();
        var studentDto = new StudentHttpDto
        {
            Id = studentId.ToString(),
            Name = "张三",
            Grade = 3,
            CreatedAt = 1700000000,
            UpdatedAt = 1700000001,
            IdentityAccountIds = new List<string> { "acc1" }
        };

        _studentClient
            .Setup(c => c.GetStudentAsync(studentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(studentDto);

        var result = await _controller.GetStudent(studentId);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.Should().BeOfType<Admin.WebApi.Models.StudentDto>();
        ((Admin.WebApi.Models.StudentDto)typed).Name.Should().Be("张三");
    }

    [Fact]
    public async Task GetStudent_NotFound_Returns404()
    {
        var studentId = Guid.NewGuid();

        _studentClient
            .Setup(c => c.GetStudentAsync(studentId.ToString(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Student not found", null, HttpStatusCode.NotFound));

        var result = await _controller.GetStudent(studentId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetStudent_InvalidArgument_ReturnsBadRequest()
    {
        var studentId = Guid.NewGuid();

        _studentClient
            .Setup(c => c.GetStudentAsync(studentId.ToString(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Invalid ID", null, HttpStatusCode.BadRequest));

        var result = await _controller.GetStudent(studentId);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
