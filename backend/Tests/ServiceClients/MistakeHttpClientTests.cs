using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.ServiceClients;

// Real-client-layer contract tests for the one Mistake client method that feeds the OSS audit
// reference aggregation (OssAuditWorker) and the pre-delete recheck (OssAuditController).
// The existing worker/controller tests mock IMistakeHttpClient, so before the #28 fix they could
// not catch the client swallowing HttpRequestException: a connection-refused degraded the audit
// to "every mistake image is an orphan" and let deletes proceed without the 502 recheck.
// These tests exercise the concrete MistakeHttpClient through a stubbed HttpMessageHandler.
public class MistakeHttpClientTests
{
    private static MistakeHttpClient CreateClient(Mock<HttpMessageHandler> handlerMock)
    {
        var client = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost:5007") };
        return new MistakeHttpClient(client, Mock.Of<ILogger<MistakeHttpClient>>());
    }

    private static Mock<HttpMessageHandler> HandlerReturning(
        HttpStatusCode statusCode, string responseBody, string mediaType = "application/json")
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, mediaType)
            });
        return handlerMock;
    }

    [Fact]
    public async Task GetMistakeItemList_ConnectionRefused_ThrowsHttpRequestException()
    {
        // Arrange — connection refused / DNS failure surfaces from HttpClient as
        // HttpRequestException. It must propagate: an empty page here would make the audit
        // aggregation treat every mistake image as an unreferenced orphan and disable the
        // pre-delete 502 recheck.
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var client = CreateClient(handlerMock);

        // Act
        var act = () => client.GetMistakeItemListAsync(string.Empty, 0, 0, MistakeReviewStatus.Unspecified, 1, 100);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetMistakeItemList_FailureEnvelope_ThrowsHttpRequestException()
    {
        // Arrange — a JSON error envelope on a non-2xx status is a failed reference fetch,
        // not "zero mistakes". Aligned with StudentHttpClient.GetAllUploadRecordsAsync.
        var handlerMock = HandlerReturning(
            HttpStatusCode.ServiceUnavailable,
            "{\"success\":false,\"message\":\"boom\"}");
        var client = CreateClient(handlerMock);

        // Act
        var act = () => client.GetMistakeItemListAsync(string.Empty, 0, 0, MistakeReviewStatus.Unspecified, 1, 100);

        // Assert
        var exception = (await act.Should().ThrowAsync<HttpRequestException>()).Subject.Single();
        exception.Message.Should().Be("boom");
        exception.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task GetMistakeItemList_SuccessEnvelope_ReturnsBoundData()
    {
        // Arrange — success path regression guard: paging data binds as-is.
        var handlerMock = HandlerReturning(
            HttpStatusCode.OK,
            "{\"success\":true,\"data\":{\"items\":[{\"id\":\"m1\",\"studentId\":\"s1\",\"sourceRegions\":[{\"sourceImagePath\":\"mistakes/a.jpg\"}]}],\"pageMeta\":{\"page\":1,\"size\":100,\"totalCount\":1,\"totalPages\":1}}}");
        var client = CreateClient(handlerMock);

        // Act
        var result = await client.GetMistakeItemListAsync(string.Empty, 0, 0, MistakeReviewStatus.Unspecified, 1, 100);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Id.Should().Be("m1");
        result.Items[0].SourceRegions.Should().ContainSingle()
            .Which.SourceImagePath.Should().Be("mistakes/a.jpg");
        result.PageMeta.TotalCount.Should().Be(1);
    }
}
