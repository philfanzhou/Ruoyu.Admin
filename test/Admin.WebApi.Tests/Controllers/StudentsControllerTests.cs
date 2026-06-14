using Admin.WebApi.Controllers;
using Admin.WebApi.Models;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Ruoyu.Study.Common.Constants;
using SProto = Ruoyu.Study.Student.Contract.Protos;
using GrpcStatusCode = Grpc.Core.StatusCode;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

public class StudentsControllerTests
{
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _grpcClient;
    private readonly Mock<ILogger<StudentsController>> _logger;
    private readonly StudentsController _controller;

    public StudentsControllerTests()
    {
        _grpcClient = new Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient>();
        _logger = new Mock<ILogger<StudentsController>>();
        _controller = new StudentsController(_grpcClient.Object, _logger.Object);
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

    private static SProto.StudentDto CreateProtoStudentDto(
        string id = "student-1",
        string name = "Test Student",
        SProto.Grade grade = SProto.Grade.Primary1,
        string[]? accountIds = null,
        long createdAt = 1000,
        long updatedAt = 2000)
    {
        var dto = new SProto.StudentDto
        {
            Id = id,
            Name = name,
            Grade = grade,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
        if (accountIds != null)
            dto.IdentityAccountIds.AddRange(accountIds);
        return dto;
    }

    // ============================================================
    // StudentCRUD Tests
    // ============================================================

    [Fact]
    public async Task ListStudents_Success_ReturnsPagedResponse()
    {
        // UT-01
        var studentDto = CreateProtoStudentDto();
        var response = new SProto.PagedStudentResponse
        {
            TotalCount = 1,
            Page = 1,
            PageSize = 20
        };
        response.Items.Add(studentDto);

        _grpcClient
            .Setup(c => c.ListStudentsAsync(
                It.IsAny<SProto.ListStudentsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var result = await _controller.ListStudents(null, null, null, null);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var paged = okResult.Value.Should().BeOfType<PagedResponse<StudentDto>>().Subject;
        paged.Items.Should().HaveCount(1);
        paged.Total.Should().Be(1);
        paged.Page.Should().Be(1);
        paged.PageSize.Should().Be(20);
        paged.Items[0].Id.Should().Be("student-1");
    }

    [Fact]
    public async Task ListStudents_PaginationNormalization_Page0_PageSize200()
    {
        // UT-02: page=0→1, pageSize=200→100
        var response = new SProto.PagedStudentResponse
        {
            TotalCount = 0,
            Page = 1,
            PageSize = 100
        };

        _grpcClient
            .Setup(c => c.ListStudentsAsync(
                It.IsAny<SProto.ListStudentsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var result = await _controller.ListStudents(null, null, 0, 200);

        result.Should().BeOfType<OkObjectResult>();
        _grpcClient.Verify(c => c.ListStudentsAsync(
            It.Is<SProto.ListStudentsRequest>(r => r.Page == 1 && r.PageSize == 100),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListStudents_FilterByNameAndGrade()
    {
        // UT-03
        var response = new SProto.PagedStudentResponse
        {
            TotalCount = 0,
            Page = 1,
            PageSize = 20
        };

        _grpcClient
            .Setup(c => c.ListStudentsAsync(
                It.IsAny<SProto.ListStudentsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var result = await _controller.ListStudents("Alice", 3, 1, 20);

        result.Should().BeOfType<OkObjectResult>();
        _grpcClient.Verify(c => c.ListStudentsAsync(
            It.Is<SProto.ListStudentsRequest>(r =>
                r.Name == "Alice" &&
                r.Grade == SProto.Grade.Primary3),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStudent_Success()
    {
        // UT-04
        var studentId = Guid.NewGuid();
        var studentDto = CreateProtoStudentDto(id: studentId.ToString(), name: "Alice");

        _grpcClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(studentDto));

        var result = await _controller.GetStudent(studentId);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<StudentDto>().Subject;
        dto.Id.Should().Be(studentId.ToString());
        dto.Name.Should().Be("Alice");
    }

    [Fact]
    public async Task GetStudent_NotFound_Returns404()
    {
        // UT-05
        var studentId = Guid.NewGuid();
        _grpcClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(GrpcStatusCode.NotFound, "Student not found")));

        var result = await _controller.GetStudent(studentId);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Student not found");
    }

    [Fact]
    public async Task CreateStudent_Success()
    {
        // UT-06
        var accountId = Guid.NewGuid().ToString();
        var studentDto = CreateProtoStudentDto(accountIds: new[] { accountId });

        _grpcClient
            .Setup(c => c.CreateStudentAsync(
                It.IsAny<SProto.CreateStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(studentDto));

        var request = new CreateStudentRequest("Alice", 1, new List<string> { accountId });
        var result = await _controller.CreateStudent(request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<StudentDto>().Subject;
        dto.Name.Should().Be("Test Student"); // from mock response
    }

    [Fact]
    public async Task CreateStudent_EmptyName_Returns400()
    {
        // UT-07
        var request = new CreateStudentRequest("", 1, new List<string> { Guid.NewGuid().ToString() });
        var result = await _controller.CreateStudent(request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Name is required.");
    }

    [Fact]
    public async Task CreateStudent_InvalidGrade_Returns400()
    {
        // UT-08
        var request = new CreateStudentRequest("Alice", 13, new List<string> { Guid.NewGuid().ToString() });
        var result = await _controller.CreateStudent(request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Invalid grade value.");
    }

    [Fact]
    public async Task CreateStudent_EmptyIdentityAccountIds_Returns400()
    {
        // UT-09
        var request = new CreateStudentRequest("Alice", 1, new List<string>());
        var result = await _controller.CreateStudent(request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("At least one Identity Account ID is required.");
    }

    [Fact]
    public async Task CreateStudent_InvalidGuidFormat_Returns400()
    {
        // UT-10
        var request = new CreateStudentRequest("Alice", 1, new List<string> { "not-a-guid" });
        var result = await _controller.CreateStudent(request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Invalid Identity Account ID format: not-a-guid");
    }

    [Fact]
    public async Task UpdateStudent_Success()
    {
        // UT-11
        var studentId = Guid.NewGuid();
        var boolResponse = new SProto.BoolResponse { Success = true };

        _grpcClient
            .Setup(c => c.UpdateStudentAsync(
                It.IsAny<SProto.UpdateStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new UpdateStudentRequest("Alice", 1, new List<string> { Guid.NewGuid().ToString() });
        var result = await _controller.UpdateStudent(studentId, request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var opResponse = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        opResponse.Success.Should().BeTrue();
        opResponse.Message.Should().Be("Student updated successfully.");
    }

    [Fact]
    public async Task UpdateStudent_GrpcSuccessFalse_Returns404()
    {
        // UT-12
        var studentId = Guid.NewGuid();
        var boolResponse = new SProto.BoolResponse { Success = false, ErrorMessage = "Not found" };

        _grpcClient
            .Setup(c => c.UpdateStudentAsync(
                It.IsAny<SProto.UpdateStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new UpdateStudentRequest("Alice", 1, null);
        var result = await _controller.UpdateStudent(studentId, request);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Not found");
    }

    [Fact]
    public async Task DeleteStudent_Success()
    {
        // UT-13
        var studentId = Guid.NewGuid();
        var boolResponse = new SProto.BoolResponse { Success = true };

        _grpcClient
            .Setup(c => c.DeleteStudentAsync(
                It.IsAny<SProto.DeleteStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var result = await _controller.DeleteStudent(studentId);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var opResponse = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        opResponse.Success.Should().BeTrue();
        opResponse.Message.Should().Be("Student deleted.");
    }

    [Fact]
    public async Task DeleteStudent_NotFound_Returns404()
    {
        // UT-14
        var studentId = Guid.NewGuid();
        var boolResponse = new SProto.BoolResponse { Success = false, ErrorMessage = "Student not found" };

        _grpcClient
            .Setup(c => c.DeleteStudentAsync(
                It.IsAny<SProto.DeleteStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var result = await _controller.DeleteStudent(studentId);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Student not found");
    }

    [Fact]
    public void GetGrades_Returns12Options()
    {
        // UT-15
        var result = _controller.GetGrades();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var grades = okResult.Value.Should().BeAssignableTo<List<GradeOption>>().Subject;
        grades.Should().HaveCount(12);
        grades.Should().BeInAscendingOrder(g => g.Value);
    }

    [Fact]
    public async Task GetSubjectOptions_Success()
    {
        // UT-16
        var response = new SProto.AvailableSubjectsResponse();
        response.Subjects.Add(new SProto.SubjectOption { Value = 1, Name = "CHINESE", DisplayName = "语文" });
        response.Subjects.Add(new SProto.SubjectOption { Value = 2, Name = "MATHEMATICS", DisplayName = "数学" });

        _grpcClient
            .Setup(c => c.GetAvailableSubjectsAsync(
                It.IsAny<SProto.Empty>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var result = await _controller.GetSubjectOptions();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var options = okResult.Value.Should().BeAssignableTo<List<SubjectOption>>().Subject;
        options.Should().HaveCount(2);
        options[0].Value.Should().Be(1);
        options[0].Name.Should().Be("CHINESE");
        options[0].DisplayName.Should().Be("语文");
    }

    // ============================================================
    // Edge Cases (EX-01 to EX-08)
    // ============================================================

    [Fact]
    public async Task ListStudents_PageNegative1_NormalizedTo1()
    {
        // EX-01
        var response = new SProto.PagedStudentResponse { TotalCount = 0, Page = 1, PageSize = 20 };

        _grpcClient
            .Setup(c => c.ListStudentsAsync(
                It.IsAny<SProto.ListStudentsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        await _controller.ListStudents(null, null, -1, 20);

        _grpcClient.Verify(c => c.ListStudentsAsync(
            It.Is<SProto.ListStudentsRequest>(r => r.Page == 1),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListStudents_PageSize0_NormalizedTo1()
    {
        // EX-02
        var response = new SProto.PagedStudentResponse { TotalCount = 0, Page = 1, PageSize = 1 };

        _grpcClient
            .Setup(c => c.ListStudentsAsync(
                It.IsAny<SProto.ListStudentsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        await _controller.ListStudents(null, null, 1, 0);

        _grpcClient.Verify(c => c.ListStudentsAsync(
            It.Is<SProto.ListStudentsRequest>(r => r.PageSize == 1),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListStudents_PageSize200_NormalizedTo100()
    {
        // EX-03
        var response = new SProto.PagedStudentResponse { TotalCount = 0, Page = 1, PageSize = 100 };

        _grpcClient
            .Setup(c => c.ListStudentsAsync(
                It.IsAny<SProto.ListStudentsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        await _controller.ListStudents(null, null, 1, 200);

        _grpcClient.Verify(c => c.ListStudentsAsync(
            It.Is<SProto.ListStudentsRequest>(r => r.PageSize == 100),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateStudent_NameOnlySpaces_Returns400()
    {
        // EX-04
        var request = new CreateStudentRequest("   ", 1, new List<string> { Guid.NewGuid().ToString() });
        var result = await _controller.CreateStudent(request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Name is required.");
    }

    [Fact]
    public async Task CreateStudent_Grade13_Returns400()
    {
        // EX-05
        var request = new CreateStudentRequest("Alice", 13, new List<string> { Guid.NewGuid().ToString() });
        var result = await _controller.CreateStudent(request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateStudent_MultipleInvalidGuids_ListedInError()
    {
        // EX-06
        var request = new CreateStudentRequest("Alice", 1, new List<string> { "bad-id-1", "bad-id-2" });
        var result = await _controller.CreateStudent(request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Contain("bad-id-1");
        error.Message.Should().Contain("bad-id-2");
    }

    [Fact]
    public async Task UpdateStudent_IdentityAccountIdsNull_SkipsValidation()
    {
        // EX-07
        var studentId = Guid.NewGuid();
        var boolResponse = new SProto.BoolResponse { Success = true };

        _grpcClient
            .Setup(c => c.UpdateStudentAsync(
                It.IsAny<SProto.UpdateStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new UpdateStudentRequest("Alice", 1, null);
        var result = await _controller.UpdateStudent(studentId, request);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ListStudents_GrpcInvalidArgument_Returns400()
    {
        // EX-08
        _grpcClient
            .Setup(c => c.ListStudentsAsync(
                It.IsAny<SProto.ListStudentsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(GrpcStatusCode.InvalidArgument, "Bad request")));

        var result = await _controller.ListStudents(null, null, null, null);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Bad request");
    }

    [Fact]
    public async Task GetStudent_GrpcInvalidArgument_Returns400()
    {
        // EX-08 variant for GetStudent
        var studentId = Guid.NewGuid();
        _grpcClient
            .Setup(c => c.GetStudentAsync(
                It.IsAny<SProto.GetStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(GrpcStatusCode.InvalidArgument, "Invalid ID")));

        var result = await _controller.GetStudent(studentId);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Invalid ID");
    }

    [Fact]
    public async Task CreateStudent_GrpcInvalidArgument_Returns400()
    {
        // EX-08 variant for CreateStudent
        _grpcClient
            .Setup(c => c.CreateStudentAsync(
                It.IsAny<SProto.CreateStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(GrpcStatusCode.InvalidArgument, "Duplicate")));

        var request = new CreateStudentRequest("Alice", 1, new List<string> { Guid.NewGuid().ToString() });
        var result = await _controller.CreateStudent(request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Duplicate");
    }

    [Fact]
    public async Task UpdateStudent_GrpcInvalidArgument_Returns400()
    {
        // EX-08 variant for UpdateStudent
        var studentId = Guid.NewGuid();
        _grpcClient
            .Setup(c => c.UpdateStudentAsync(
                It.IsAny<SProto.UpdateStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(GrpcStatusCode.InvalidArgument, "Bad update")));

        var request = new UpdateStudentRequest("Alice", 1, null);
        var result = await _controller.UpdateStudent(studentId, request);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteStudent_GrpcInvalidArgument_Returns400()
    {
        // EX-08 variant for DeleteStudent
        var studentId = Guid.NewGuid();
        _grpcClient
            .Setup(c => c.DeleteStudentAsync(
                It.IsAny<SProto.DeleteStudentRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(GrpcStatusCode.InvalidArgument, "Bad delete")));

        var result = await _controller.DeleteStudent(studentId);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}

// ============================================================
// Account Linking Tests
// ============================================================

public class AccountLinkingTests
{
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _grpcClient;
    private readonly Mock<ILogger<StudentsController>> _logger;
    private readonly StudentsController _controller;

    public AccountLinkingTests()
    {
        _grpcClient = new Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient>();
        _logger = new Mock<ILogger<StudentsController>>();
        _controller = new StudentsController(_grpcClient.Object, _logger.Object);
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

    [Fact]
    public async Task GetIdentityAccountsByStudentId_Success()
    {
        // UT-01
        var studentId = Guid.NewGuid();
        var response = new SProto.AccountListResponse();
        response.AccountIds.AddRange(new[] { "account-1", "account-2" });

        _grpcClient
            .Setup(c => c.GetIdentityAccountsByStudentIdAsync(
                It.IsAny<SProto.GetAccountsByStudentIdRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var result = await _controller.GetIdentityAccountsByStudentId(studentId);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var accounts = okResult.Value.Should().BeAssignableTo<IReadOnlyList<string>>().Subject;
        accounts.Should().HaveCount(2);
        accounts.Should().Contain("account-1", "account-2");
    }

    [Fact]
    public async Task LinkIdentityAccountToStudent_Success()
    {
        // UT-02
        var studentId = Guid.NewGuid();
        var accountId = Guid.NewGuid().ToString();
        var boolResponse = new SProto.BoolResponse { Success = true };

        _grpcClient
            .Setup(c => c.LinkIdentityAccountToStudentAsync(
                It.IsAny<SProto.LinkAccountRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new LinkUserRequest(accountId);
        var result = await _controller.LinkIdentityAccountToStudent(studentId, request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var opResponse = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        opResponse.Success.Should().BeTrue();
        opResponse.Message.Should().Be("Identity account linked to student successfully.");
    }

    [Fact]
    public async Task LinkIdentityAccount_EmptyIdentityAccountId_Returns400()
    {
        // UT-03
        var studentId = Guid.NewGuid();
        var request = new LinkUserRequest("");

        var result = await _controller.LinkIdentityAccountToStudent(studentId, request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Identity Account ID is required.");
    }

    [Fact]
    public async Task LinkIdentityAccount_InvalidGuid_Returns400()
    {
        // UT-04
        var studentId = Guid.NewGuid();
        var request = new LinkUserRequest("not-a-guid");

        var result = await _controller.LinkIdentityAccountToStudent(studentId, request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Invalid Identity Account ID format. Must be a valid GUID.");
    }

    [Fact]
    public async Task LinkIdentityAccount_GrpcSuccessFalse_Returns404()
    {
        // UT-05
        var studentId = Guid.NewGuid();
        var accountId = Guid.NewGuid().ToString();
        var boolResponse = new SProto.BoolResponse { Success = false, ErrorMessage = "Student not found" };

        _grpcClient
            .Setup(c => c.LinkIdentityAccountToStudentAsync(
                It.IsAny<SProto.LinkAccountRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new LinkUserRequest(accountId);
        var result = await _controller.LinkIdentityAccountToStudent(studentId, request);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Student not found");
    }

    [Fact]
    public async Task LinkIdentityAccount_GrpcInvalidArgument_Returns400()
    {
        // UT-06
        var studentId = Guid.NewGuid();
        var accountId = Guid.NewGuid().ToString();

        _grpcClient
            .Setup(c => c.LinkIdentityAccountToStudentAsync(
                It.IsAny<SProto.LinkAccountRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(GrpcStatusCode.InvalidArgument, "Already linked")));

        var request = new LinkUserRequest(accountId);
        var result = await _controller.LinkIdentityAccountToStudent(studentId, request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Already linked");
    }

    [Fact]
    public async Task UnlinkIdentityAccount_Success()
    {
        // UT-07
        var studentId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var boolResponse = new SProto.BoolResponse { Success = true };

        _grpcClient
            .Setup(c => c.UnlinkIdentityAccountFromStudentAsync(
                It.IsAny<SProto.UnlinkAccountRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var result = await _controller.UnlinkIdentityAccountFromStudent(studentId, accountId);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var opResponse = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        opResponse.Success.Should().BeTrue();
        opResponse.Message.Should().Be("Identity account unlinked from student.");
    }

    [Fact]
    public async Task UnlinkIdentityAccount_GrpcSuccessFalse_Returns404()
    {
        // UT-08
        var studentId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var boolResponse = new SProto.BoolResponse { Success = false, ErrorMessage = "Not linked" };

        _grpcClient
            .Setup(c => c.UnlinkIdentityAccountFromStudentAsync(
                It.IsAny<SProto.UnlinkAccountRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var result = await _controller.UnlinkIdentityAccountFromStudent(studentId, accountId);

        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var error = notFound.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Not linked");
    }

    [Fact]
    public async Task UnlinkIdentityAccount_GrpcInvalidArgument_Returns400()
    {
        // UT-09
        var studentId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        _grpcClient
            .Setup(c => c.UnlinkIdentityAccountFromStudentAsync(
                It.IsAny<SProto.UnlinkAccountRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(GrpcStatusCode.InvalidArgument, "Invalid account")));

        var result = await _controller.UnlinkIdentityAccountFromStudent(studentId, accountId);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Invalid account");
    }

    [Fact]
    public async Task LinkIdentityAccount_WhitespaceOnlyIdentityAccountId_Returns400()
    {
        // EX-01
        var studentId = Guid.NewGuid();
        var request = new LinkUserRequest("   ");

        var result = await _controller.LinkIdentityAccountToStudent(studentId, request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Identity Account ID is required.");
    }
}

// ============================================================
// Open Subject Management Tests
// ============================================================

public class OpenSubjectManagementTests
{
    private readonly Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient> _grpcClient;
    private readonly Mock<ILogger<StudentsController>> _logger;
    private readonly StudentsController _controller;

    public OpenSubjectManagementTests()
    {
        _grpcClient = new Mock<SProto.StudentLearningGrpcService.StudentLearningGrpcServiceClient>();
        _logger = new Mock<ILogger<StudentsController>>();
        _controller = new StudentsController(_grpcClient.Object, _logger.Object);
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

    [Fact]
    public async Task GetStudentOpenSubjects_Success()
    {
        // UT-01
        var studentId = Guid.NewGuid();
        var response = new SProto.OpenSubjectsResponse();
        response.Subjects.Add(new SProto.OpenSubjectDto
        {
            Id = "os-1",
            Subject = 1,
            OpenStartDate = "2024-01-01",
            OpenEndDate = "2024-12-31",
            IsActive = true
        });

        _grpcClient
            .Setup(c => c.GetStudentOpenSubjectsAsync(
                It.IsAny<SProto.GetStudentOpenSubjectsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var result = await _controller.GetStudentOpenSubjects(studentId, null);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = okResult.Value.Should().BeAssignableTo<List<OpenSubjectDto>>().Subject;
        dtos.Should().HaveCount(1);
        dtos[0].Id.Should().Be("os-1");
        dtos[0].Subject.Should().Be(1);
        dtos[0].OpenStartDate.Should().Be("2024-01-01");
        dtos[0].OpenEndDate.Should().Be("2024-12-31");
        dtos[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetStudentOpenSubjects_ActiveOnlyTrue()
    {
        // UT-02
        var studentId = Guid.NewGuid();
        var response = new SProto.OpenSubjectsResponse();

        _grpcClient
            .Setup(c => c.GetStudentOpenSubjectsAsync(
                It.IsAny<SProto.GetStudentOpenSubjectsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        await _controller.GetStudentOpenSubjects(studentId, true);

        _grpcClient.Verify(c => c.GetStudentOpenSubjectsAsync(
            It.Is<SProto.GetStudentOpenSubjectsRequest>(r => r.ActiveOnly == true),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStudentOpenSubjects_EmptyEndDate_MapsToNull()
    {
        // UT-03
        var studentId = Guid.NewGuid();
        var response = new SProto.OpenSubjectsResponse();
        response.Subjects.Add(new SProto.OpenSubjectDto
        {
            Id = "os-2",
            Subject = 2,
            OpenStartDate = "2024-01-01",
            OpenEndDate = "",
            IsActive = true
        });

        _grpcClient
            .Setup(c => c.GetStudentOpenSubjectsAsync(
                It.IsAny<SProto.GetStudentOpenSubjectsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(response));

        var result = await _controller.GetStudentOpenSubjects(studentId, null);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = okResult.Value.Should().BeAssignableTo<List<OpenSubjectDto>>().Subject;
        dtos[0].OpenEndDate.Should().BeNull();
    }

    [Fact]
    public async Task SetStudentOpenSubjects_Success()
    {
        // UT-04
        var studentId = Guid.NewGuid();
        var boolResponse = new SProto.BoolResponse { Success = true };

        _grpcClient
            .Setup(c => c.SetStudentOpenSubjectsAsync(
                It.IsAny<SProto.SetOpenSubjectsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new SetOpenSubjectsRequest(new List<SubjectItem>
        {
            new(1, "2024-01-01", "2024-12-31")
        });

        var result = await _controller.SetStudentOpenSubjects(studentId, request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var opResponse = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        opResponse.Success.Should().BeTrue();
        opResponse.Message.Should().Be("Open subjects updated successfully.");
    }

    [Fact]
    public async Task SetStudentOpenSubjects_InvalidSubjectValue_Returns400()
    {
        // UT-05
        var studentId = Guid.NewGuid();
        var request = new SetOpenSubjectsRequest(new List<SubjectItem>
        {
            new(99, "2024-01-01", null)
        });

        var result = await _controller.SetStudentOpenSubjects(studentId, request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Invalid subject value: 99");
    }

    [Fact]
    public async Task SetStudentOpenSubjects_EmptyOpenStartDate_Returns400()
    {
        // UT-06
        var studentId = Guid.NewGuid();
        var request = new SetOpenSubjectsRequest(new List<SubjectItem>
        {
            new(1, "", null)
        });

        var result = await _controller.SetStudentOpenSubjects(studentId, request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Open start date is required");
    }

    [Fact]
    public async Task SetStudentOpenSubjects_InvalidOpenStartDateFormat_Returns400()
    {
        // UT-07
        var studentId = Guid.NewGuid();
        var request = new SetOpenSubjectsRequest(new List<SubjectItem>
        {
            new(1, "not-a-date", null)
        });

        var result = await _controller.SetStudentOpenSubjects(studentId, request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Invalid start date format: not-a-date");
    }

    [Fact]
    public async Task SetStudentOpenSubjects_InvalidOpenEndDateFormat_Returns400()
    {
        // UT-08
        var studentId = Guid.NewGuid();
        var request = new SetOpenSubjectsRequest(new List<SubjectItem>
        {
            new(1, "2024-01-01", "bad-end-date")
        });

        var result = await _controller.SetStudentOpenSubjects(studentId, request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Invalid end date format: bad-end-date");
    }

    [Fact]
    public async Task SetStudentOpenSubjects_GrpcSuccessFalse_Returns400()
    {
        // UT-09
        var studentId = Guid.NewGuid();
        var boolResponse = new SProto.BoolResponse { Success = false, ErrorMessage = "Update failed" };

        _grpcClient
            .Setup(c => c.SetStudentOpenSubjectsAsync(
                It.IsAny<SProto.SetOpenSubjectsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new SetOpenSubjectsRequest(new List<SubjectItem>
        {
            new(1, "2024-01-01", null)
        });

        var result = await _controller.SetStudentOpenSubjects(studentId, request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Update failed");
    }

    [Fact]
    public async Task SetStudentOpenSubjects_GrpcInvalidArgument_Returns400()
    {
        // UT-10
        var studentId = Guid.NewGuid();

        _grpcClient
            .Setup(c => c.SetStudentOpenSubjectsAsync(
                It.IsAny<SProto.SetOpenSubjectsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new RpcException(new Status(GrpcStatusCode.InvalidArgument, "Bad request")));

        var request = new SetOpenSubjectsRequest(new List<SubjectItem>
        {
            new(1, "2024-01-01", null)
        });

        var result = await _controller.SetStudentOpenSubjects(studentId, request);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = badRequest.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Message.Should().Be("Bad request");
    }

    [Fact]
    public async Task SetStudentOpenSubjects_EmptySubjectsList_SkipsValidation_CallsGrpc()
    {
        // EX-01
        var studentId = Guid.NewGuid();
        var boolResponse = new SProto.BoolResponse { Success = true };

        _grpcClient
            .Setup(c => c.SetStudentOpenSubjectsAsync(
                It.IsAny<SProto.SetOpenSubjectsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncCall(boolResponse));

        var request = new SetOpenSubjectsRequest(new List<SubjectItem>());
        var result = await _controller.SetStudentOpenSubjects(studentId, request);

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var opResponse = okResult.Value.Should().BeOfType<OperationResponse>().Subject;
        opResponse.Success.Should().BeTrue();

        _grpcClient.Verify(c => c.SetStudentOpenSubjectsAsync(
            It.Is<SProto.SetOpenSubjectsRequest>(r => r.StudentId == studentId.ToString()),
            It.IsAny<Metadata>(),
            It.IsAny<DateTime?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetStudentOpenSubjects_NullOpenEndDate_SendsEmptyStringInGrpcRequest()
    {
        // EX-04
        var studentId = Guid.NewGuid();
        var boolResponse = new SProto.BoolResponse { Success = true };

        SProto.SetOpenSubjectsRequest? capturedRequest = null;
        _grpcClient
            .Setup(c => c.SetStudentOpenSubjectsAsync(
                It.IsAny<SProto.SetOpenSubjectsRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback<SProto.SetOpenSubjectsRequest, Metadata, DateTime?, CancellationToken>(
                (r, _, _, _) => capturedRequest = r)
            .Returns(CreateAsyncCall(boolResponse));

        var request = new SetOpenSubjectsRequest(new List<SubjectItem>
        {
            new(1, "2024-01-01", null)
        });

        await _controller.SetStudentOpenSubjects(studentId, request);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Subjects.Should().HaveCount(1);
        capturedRequest.Subjects[0].OpenEndDate.Should().BeEmpty();
    }
}

// ============================================================
// Open Subject Model Tests
// ============================================================

public class OpenSubjectModelTests
{
    [Fact]
    public void OpenSubjectDto_Constructor_SetsProperties()
    {
        var dto = new OpenSubjectDto("id-1", 1, "2024-01-01", "2024-12-31", true);

        dto.Id.Should().Be("id-1");
        dto.Subject.Should().Be(1);
        dto.OpenStartDate.Should().Be("2024-01-01");
        dto.OpenEndDate.Should().Be("2024-12-31");
        dto.IsActive.Should().BeTrue();
    }

    [Fact]
    public void OpenSubjectDto_WithNullOpenEndDate_SetsProperty()
    {
        var dto = new OpenSubjectDto("id-2", 2, "2024-01-01", null, false);

        dto.OpenEndDate.Should().BeNull();
        dto.IsActive.Should().BeFalse();
    }

    [Fact]
    public void SubjectItem_Constructor_SetsProperties()
    {
        var item = new SubjectItem(1, "2024-01-01", "2024-12-31");

        item.Subject.Should().Be(1);
        item.OpenStartDate.Should().Be("2024-01-01");
        item.OpenEndDate.Should().Be("2024-12-31");
    }

    [Fact]
    public void SetOpenSubjectsRequest_Constructor_SetsProperties()
    {
        var subjects = new List<SubjectItem> { new(1, "2024-01-01", null) };
        var request = new SetOpenSubjectsRequest(subjects);

        request.Subjects.Should().HaveCount(1);
        request.Subjects[0].Subject.Should().Be(1);
    }

    [Fact]
    public void SubjectOption_Constructor_SetsProperties()
    {
        var option = new SubjectOption(1, "CHINESE", "语文");

        option.Value.Should().Be(1);
        option.Name.Should().Be("CHINESE");
        option.DisplayName.Should().Be("语文");
    }
}
