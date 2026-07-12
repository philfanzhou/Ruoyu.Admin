using System.Net;
using System.Text;
using Admin.WebApi.Controllers;
using Admin.WebApi.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Ruoyu.Study.MistakeBff.GrpcClients;
using Xunit;

namespace Admin.WebApi.Tests.Controllers;

public class IdentityAccountsControllerTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactory;
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly Mock<IOptions<IdentityServiceOptions>> _optionsMock;
    private readonly Mock<IStudentHttpClient> _studentClient;
    private readonly Mock<ILogger<IdentityAccountsController>> _logger;
    private readonly IdentityServiceOptions _options;
    private readonly IdentityAccountsController _controller;

    public IdentityAccountsControllerTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _options = new IdentityServiceOptions
        {
            Address = "http://localhost:5002",
            AppId = "test-app",
            AppSecret = "test-secret"
        };
        _optionsMock = new Mock<IOptions<IdentityServiceOptions>>();
        _optionsMock.Setup(o => o.Value).Returns(_options);
        _studentClient = new Mock<IStudentHttpClient>();
        _logger = new Mock<ILogger<IdentityAccountsController>>();

        var client = new HttpClient(_handlerMock.Object);
        _httpClientFactory = new Mock<IHttpClientFactory>();
        _httpClientFactory.Setup(f => f.CreateClient("IdentityService")).Returns(client);

        _controller = new IdentityAccountsController(
            _httpClientFactory.Object,
            _optionsMock.Object,
            _studentClient.Object,
            _logger.Object);
    }

    // ============================================================
    // Helper methods
    // ============================================================

    private void SetupHttpHandler(HttpStatusCode statusCode, string responseBody)
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
    }

    private void SetupHttpHandlerException(Exception exception)
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(exception);
    }

    private static string BuildBatchResponseJson(params (string userId, string username, string phone, string remark, string displayName)[] users)
    {
        var items = users.Select(u =>
            $"{{\"userId\":\"{u.userId}\",\"username\":\"{u.username}\",\"phone\":\"{u.phone}\",\"remark\":\"{u.remark}\",\"displayName\":\"{u.displayName}\"}}");
        return $"[{string.Join(",", items)}]";
    }

    // ============================================================
    // GetIdentityAccountsBatch Tests
    // ============================================================

    [Fact]
    public async Task GetIdentityAccountsBatch_NullInput_ReturnsEmptyAccountsWithNoFailure()
    {
        // Act
        var result = await _controller.GetIdentityAccountsBatch(null!);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        var accounts = (IReadOnlyList<IdentityAccountDto>)data.accounts;
        accounts.Should().BeEmpty();
        ((bool)data.partialFailure).Should().BeFalse();
        ((string?)data.warning).Should().BeNull();
    }

    [Fact]
    public async Task GetIdentityAccountsBatch_EmptyInput_ReturnsEmptyAccountsWithNoFailure()
    {
        // Act
        var result = await _controller.GetIdentityAccountsBatch(new List<string>());

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        var accounts = (IReadOnlyList<IdentityAccountDto>)data.accounts;
        accounts.Should().BeEmpty();
        ((bool)data.partialFailure).Should().BeFalse();
        ((string?)data.warning).Should().BeNull();
    }

    [Fact]
    public async Task GetIdentityAccountsBatch_MissingCredentials_ReturnsEmptyAccountsWithNoFailure()
    {
        // Arrange
        _options.AppId = "";
        _options.AppSecret = "";

        // Act
        var result = await _controller.GetIdentityAccountsBatch(new List<string> { "user-1" });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        var accounts = (IReadOnlyList<IdentityAccountDto>)data.accounts;
        accounts.Should().BeEmpty();
        ((bool)data.partialFailure).Should().BeFalse();
        ((string?)data.warning).Should().BeNull();
    }

    [Fact]
    public async Task GetIdentityAccountsBatch_Success_ReturnsFilteredAccounts()
    {
        // Arrange
        var json = BuildBatchResponseJson(
            ("user-1", "john", "13800138000", "VIP", "John Doe"),
            ("user-2", "jane", "13900139000", "", "Jane Doe"));
        SetupHttpHandler(HttpStatusCode.OK, json);

        // Act
        var result = await _controller.GetIdentityAccountsBatch(new List<string> { "user-1", "user-2" });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        var accounts = (IReadOnlyList<IdentityAccountDto>)data.accounts;
        accounts.Should().HaveCount(2);
        ((bool)data.partialFailure).Should().BeFalse();
        ((string?)data.warning).Should().BeNull();

        accounts[0].UserId.Should().Be("user-1");
        accounts[0].Username.Should().Be("john");
        accounts[0].DisplayName.Should().Be("John Doe");
        accounts[0].Phone.Should().Be("13800138000");
        accounts[0].Remark.Should().Be("VIP");

        accounts[1].UserId.Should().Be("user-2");
        accounts[1].Username.Should().Be("jane");
        accounts[1].DisplayName.Should().Be("Jane Doe");
        accounts[1].Phone.Should().Be("13900139000");
        accounts[1].Remark.Should().Be("");
    }

    [Fact]
    public async Task GetIdentityAccountsBatch_FiltersOutUsersNotInTargetIds()
    {
        // Arrange
        var json = BuildBatchResponseJson(
            ("user-1", "john", "13800138000", "VIP", "John Doe"),
            ("user-2", "jane", "13900139000", "", "Jane Doe"),
            ("user-3", "bob", "13700137000", "", "Bob"));
        SetupHttpHandler(HttpStatusCode.OK, json);

        // Act - only request user-1 and user-3
        var result = await _controller.GetIdentityAccountsBatch(new List<string> { "user-1", "user-3" });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        var accounts = (IReadOnlyList<IdentityAccountDto>)data.accounts;
        accounts.Should().HaveCount(2);
        accounts.Should().Contain(a => a.UserId == "user-1");
        accounts.Should().Contain(a => a.UserId == "user-3");
        accounts.Should().NotContain(a => a.UserId == "user-2");
    }

    [Fact]
    public async Task GetIdentityAccountsBatch_CaseInsensitiveMatching()
    {
        // Arrange
        var json = BuildBatchResponseJson(
            ("User-1", "john", "13800138000", "", "John Doe"));
        SetupHttpHandler(HttpStatusCode.OK, json);

        // Act - request with different casing
        var result = await _controller.GetIdentityAccountsBatch(new List<string> { "user-1" });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        var accounts = (IReadOnlyList<IdentityAccountDto>)data.accounts;
        accounts.Should().HaveCount(1);
        accounts[0].UserId.Should().Be("User-1");
    }

    [Fact]
    public async Task GetIdentityAccountsBatch_NullFieldsDefaultToEmptyString()
    {
        // Arrange - JSON with null fields
        var json = "[{\"userId\":\"user-1\",\"username\":null,\"phone\":null,\"remark\":null,\"displayName\":null}]";
        SetupHttpHandler(HttpStatusCode.OK, json);

        // Act
        var result = await _controller.GetIdentityAccountsBatch(new List<string> { "user-1" });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        var accounts = (IReadOnlyList<IdentityAccountDto>)data.accounts;
        accounts.Should().HaveCount(1);
        accounts[0].Username.Should().Be("");
        accounts[0].Phone.Should().Be("");
        accounts[0].Remark.Should().Be("");
        accounts[0].DisplayName.Should().Be("");
    }

    [Fact]
    public async Task GetIdentityAccountsBatch_NonSuccessStatusCode_ReturnsEmptyAccountsWithNoFailure()
    {
        // Arrange
        SetupHttpHandler(HttpStatusCode.InternalServerError, "");

        // Act
        var result = await _controller.GetIdentityAccountsBatch(new List<string> { "user-1" });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        var accounts = (IReadOnlyList<IdentityAccountDto>)data.accounts;
        accounts.Should().BeEmpty();
        ((bool)data.partialFailure).Should().BeFalse();
        ((string?)data.warning).Should().BeNull();
    }

    [Fact]
    public async Task GetIdentityAccountsBatch_NullDeserializedResponse_ReturnsEmptyAccountsWithNoFailure()
    {
        // Arrange - return "null" JSON which deserializes to null for a List type
        SetupHttpHandler(HttpStatusCode.OK, "null");

        // Act
        var result = await _controller.GetIdentityAccountsBatch(new List<string> { "user-1" });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        var accounts = (IReadOnlyList<IdentityAccountDto>)data.accounts;
        accounts.Should().BeEmpty();
        ((bool)data.partialFailure).Should().BeFalse();
        ((string?)data.warning).Should().BeNull();
    }

    [Fact]
    public async Task GetIdentityAccountsBatch_ExceptionDuringHttp_ReturnsPartialFailureWithWarning()
    {
        // Arrange
        SetupHttpHandlerException(new HttpRequestException("Connection refused"));

        // Act
        var result = await _controller.GetIdentityAccountsBatch(new List<string> { "user-1" });

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        dynamic data = okResult.Value!;
        var accounts = (IReadOnlyList<IdentityAccountDto>)data.accounts;
        accounts.Should().BeEmpty();
        ((bool)data.partialFailure).Should().BeTrue();
        ((string?)data.warning).Should().Be("Identity 服务暂时不可用，部分账户信息无法加载");
    }

    // ============================================================
    // GetStudentsByIdentityAccountId Tests
    // ============================================================

    [Fact]
    public async Task GetStudentsByIdentityAccountId_Success_ReturnsStudentDtos()
    {
        // Arrange
        var accountId = Guid.NewGuid();

        var protoStudent = new StudentInfoDto
        {
            Id = "student-1",
            Name = "Alice",
            Grade = 3,
            CreatedAt = 1000,
            UpdatedAt = 2000
        };
        protoStudent.IdentityAccountIds.AddRange(new[] { accountId.ToString() });

        var response = new StudentsByAccountResult();
        response.Students.Add(protoStudent);

        _studentClient
            .Setup(c => c.GetStudentsByIdentityAccountIdAsync(accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetStudentsByIdentityAccountId(accountId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = okResult.Value.Should().BeAssignableTo<IReadOnlyList<Admin.WebApi.Models.StudentDto>>().Subject;
        dtos.Should().HaveCount(1);
        dtos[0].Id.Should().Be("student-1");
        dtos[0].Name.Should().Be("Alice");
        dtos[0].Grade.Should().Be(3);
        dtos[0].IdentityAccountIds.Should().Contain(accountId.ToString());
        dtos[0].CreatedAt.Should().Be(1000);
        dtos[0].UpdatedAt.Should().Be(2000);
    }

    [Fact]
    public async Task GetStudentsByIdentityAccountId_Failure_ReturnsEmptyList()
    {
        // Arrange - HTTP client catches exceptions and returns empty result on failure
        var accountId = Guid.NewGuid();

        _studentClient
            .Setup(c => c.GetStudentsByIdentityAccountIdAsync(accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StudentsByAccountResult());

        // Act
        var result = await _controller.GetStudentsByIdentityAccountId(accountId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dtos = okResult.Value.Should().BeAssignableTo<IReadOnlyList<Admin.WebApi.Models.StudentDto>>().Subject;
        dtos.Should().BeEmpty();
    }
}
