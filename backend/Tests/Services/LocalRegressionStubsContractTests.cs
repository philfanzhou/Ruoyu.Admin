extern alias LocalRegressionStubs;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LocalRegressionStubs::Ruoyu.Admin.LocalRegressionStubs;
using Ruoyu.Admin.ServiceClients;
using Xunit;

namespace Admin.WebApi.Tests.Services;

/// <summary>
/// Interface compatibility tests: check that the local regression stubs' (backend/Tools/LocalRegressionStubs)
/// legacy list responses deserialize into the designated ServiceClients DTOs with the designated
/// JsonSerializerOptions, catching response/client drift early.
/// These tests do not validate the integrity of the current StorageReferences v1 references;
/// see docs/development/LocalRegressionEnv.md for the regression environment.
/// </summary>
public class LocalRegressionStubsContractTests
{
    // Mirrors the options used by StudentHttpClient / MistakeHttpClient.
    private static readonly JsonSerializerOptions ClientJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int StudentPort = 15005;
    private const int MistakePort = 15007;
    private const int HomeworkPort = 15009;

    [Fact]
    public async Task StubResponses_BindToTheDtosTheAuditAggregates()
    {
        await using var app = StubServer.Build(
            RegressionFixtures.Build(),
            mistakeOutage: false,
            StudentPort,
            MistakePort,
            HomeworkPort);
        await app.StartAsync();
        try
        {
            using var http = new HttpClient();

            // Student contract: StudentHttpClient.GetAllUploadRecordsAsync binds the
            // success/data envelope and reads items[].imageEntries[].path + totalCount.
            using var uploadsResponse = await http.GetAsync(
                $"http://localhost:{StudentPort}/api/uploads?page=1&pageSize=100");
            uploadsResponse.EnsureSuccessStatusCode();
            var uploadsEnvelope = JsonDocument.Parse(
                await uploadsResponse.Content.ReadAsStringAsync());
            uploadsEnvelope.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            var uploads = uploadsEnvelope.RootElement.GetProperty("data").Deserialize(
                typeof(UploadRecordsPage), ClientJsonOptions) as UploadRecordsPage;
            uploads.Should().NotBeNull();
            uploads!.Items.Should().ContainSingle().Which.ImageEntries
                .Should().ContainSingle(entry => entry.Path == RegressionFixtures.UploadReferencedPath);
            uploads.TotalCount.Should().Be(1);

            // Mistake contract: MistakeHttpClient.GetMistakeItemListAsync reads
            // items[].sourceRegions[].sourceImagePath + pageMeta.totalCount.
            using var mistakesResponse = await http.GetAsync(
                $"http://localhost:{MistakePort}/api/mistakes?page=1&size=100");
            mistakesResponse.EnsureSuccessStatusCode();
            var mistakesEnvelope = JsonDocument.Parse(
                await mistakesResponse.Content.ReadAsStringAsync());
            mistakesEnvelope.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            var mistakes = mistakesEnvelope.RootElement.GetProperty("data").Deserialize(
                typeof(MistakeItemPageResult), ClientJsonOptions) as MistakeItemPageResult;
            mistakes.Should().NotBeNull();
            mistakes!.Items.Should().ContainSingle().Which.SourceRegions
                .Should().ContainSingle(region =>
                    region.SourceImagePath == RegressionFixtures.MistakeReferencedPath);
            mistakes.PageMeta.TotalCount.Should().Be(1);

            // Homework contract: HomeworkReferenceClient.GetAllImagePathsAsync reads the
            // success envelope plus data.paths[] and data.hasMore.
            using var homeworkResponse = await http.GetAsync(
                $"http://localhost:{HomeworkPort}/api/admin/storage/image-references?page=1&size=200");
            homeworkResponse.EnsureSuccessStatusCode();
            var homework = JsonDocument.Parse(
                await homeworkResponse.Content.ReadAsStringAsync());
            homework.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            var homeworkData = homework.RootElement.GetProperty("data");
            homeworkData.GetProperty("paths").EnumerateArray()
                .Select(element => element.GetString())
                .Should().Contain(RegressionFixtures.HomeworkReferencedPath);
            homeworkData.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task MistakeOutage_ReturnsNonJson503_MistakeHttpClientCannotDeserialize()
    {
        await using var app = StubServer.Build(
            RegressionFixtures.Build(),
            mistakeOutage: true,
            StudentPort,
            MistakePort,
            HomeworkPort);
        await app.StartAsync();
        try
        {
            using var http = new HttpClient();

            using var response = await http.GetAsync(
                $"http://localhost:{MistakePort}/api/mistakes?page=1&size=100");

            // The outage simulation must fail deserialization (JsonException), not be caught
            // by MistakeHttpClient's HttpRequestException handler, so the failure propagates
            // to OssAuditWorker / OssAuditController and yields the "Mistake service unavailable"
            // abort / 502 branch.
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            var body = await response.Content.ReadAsStringAsync();
            var act = () => JsonDocument.Parse(body);
            act.Should().Throw<JsonException>("the outage body must not be parseable as JSON");
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
