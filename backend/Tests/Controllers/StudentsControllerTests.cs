using Admin.WebApi.Controllers;
using Admin.WebApi.Models;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

/// <summary>
/// StudentsController UT：覆盖 ListStudents、CreateStudent、GetStudent。
/// </summary>
public class StudentsControllerTests
{
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _grpcClient;
    private readonly Mock<ILogger<StudentsController>> _logger;
    private readonly StudentsController _controller;

    public StudentsControllerTests()
    {
        _grpcClient = new Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient>();
        _logger = new Mock<ILogger<StudentsController>>();

        _controller = new StudentsController(
            _grpcClient.Object,
            _logger.Object
        );
    }

    private static AsyncUnaryCall<T> CreateAsyncCall<T>(T response) where T : class
    {
        return new AsyncUnaryCall<T>(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    // ============================================================
    // ListStudents
    // ============================================================

    [Fact]
    public async Task ListStudents_ReturnsStudents()
    {
        var studentDto1 = new SProto.StudentDto
        {
            Id = "s1",
            Name = "张三",
            Grade = SProto.Grade.Primary3,
            CreatedAt = 1700000000,
            UpdatedAt = 1700000001
        };
        studentDto1.IdentityAccountIds.Add("acc1");

        var studentDto2 = new SProto.StudentDto
        {
            Id = "s2",
            Name = "李四",
            Grade = SProto.Grade.Primary5,
            CreatedAt = 1700000002,
            UpdatedAt = 1700000003
        };
        studentDto2.IdentityAccountIds.Add("acc2");

        var response = new SProto.PagedStudentResponse
        {
            TotalCount = 2,
            Page = 1,
            PageSize = 20
        };
        response.Items.Add(studentDto1);
        response.Items.Add(studentDto2);

        _grpcClient
            .Setup(c => c.ListStudentsAsync(
                It.IsAny<SProto.ListStudentsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var result = await _controller.ListStudents(null, null, null, null);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        // PagedResponse<StudentDto> has Items property
        var items = (IReadOnlyList<StudentDto>)typed.GetType().GetProperty("Items")!.GetValue(typed)!;
        items.Should().HaveCount(2);
        items[0].Name.Should().Be("张三");
        items[1].Name.Should().Be("李四");
    }

    [Fact]
    public async Task ListStudents_InvalidArgument_ReturnsBadRequest()
    {
        _grpcClient
            .Setup(c => c.ListStudentsAsync(
                It.IsAny<SProto.ListStudentsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.InvalidArgument, "Invalid page")));

        var result = await _controller.ListStudents(null, null, null, null);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ============================================================
    // CreateStudent
    // ============================================================

    [Fact]
    public async Task CreateStudent_ValidRequest_ReturnsCreatedStudent()
    {
        var createdStudent = new SProto.StudentDto
        {
            Id = Guid.NewGuid().ToString(),
            Name = "王五",
            Grade = SProto.Grade.Primary3,
            CreatedAt = 1700000000,
            UpdatedAt = 1700000001
        };
        createdStudent.IdentityAccountIds.Add("acc1");

        _grpcClient
            .Setup(c => c.CreateStudentAsync(
                It.IsAny<SProto.CreateStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(createdStudent));

        var request = new CreateStudentRequest("王五", 3, new List<string> { Guid.NewGuid().ToString() });

        var result = await _controller.CreateStudent(request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.Should().BeOfType<StudentDto>();
        ((StudentDto)typed).Name.Should().Be("王五");
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
        var studentDto = new SProto.StudentDto
        {
            Id = studentId.ToString(),
            Name = "张三",
            Grade = SProto.Grade.Primary3,
            CreatedAt = 1700000000,
            UpdatedAt = 1700000001
        };
        studentDto.IdentityAccountIds.Add("acc1");

        _grpcClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(studentDto));

        var result = await _controller.GetStudent(studentId);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var typed = okResult.Value!;
        typed.Should().BeOfType<StudentDto>();
        ((StudentDto)typed).Name.Should().Be("张三");
    }

    [Fact]
    public async Task GetStudent_NotFound_Returns404()
    {
        var studentId = Guid.NewGuid();

        _grpcClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.NotFound, "Student not found")));

        var result = await _controller.GetStudent(studentId);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetStudent_InvalidArgument_ReturnsBadRequest()
    {
        var studentId = Guid.NewGuid();

        _grpcClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(StatusCode.InvalidArgument, "Invalid ID")));

        var result = await _controller.GetStudent(studentId);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
