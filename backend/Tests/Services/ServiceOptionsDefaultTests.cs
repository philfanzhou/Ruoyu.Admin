using Admin.WebApi;
using Admin.WebApi.Services;
using Microsoft.Extensions.Configuration;
using Ruoyu.Admin.Common.Oss;
using Xunit;

namespace Admin.WebApi.Tests.Services;

/// <summary>
/// Pins the consolidated configuration defaults (issue #117): each options class is the
/// single authoritative home of a default that previously lived in an inline Program.cs /
/// OssAuditWorker fallback expression or in the base appsettings.json. The values must stay
/// identical to the former fallbacks so the effective configuration is unchanged.
/// </summary>
public sealed class ServiceOptionsDefaultTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] entries)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (key, value) in entries)
        {
            values[key] = value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void DownstreamServiceOptions_DefaultsMatchFormerProgramFallbacks()
    {
        Assert.Equal("http://localhost:5005", new StudentServiceOptions().Url);
        Assert.Equal("http://localhost:5007", new MistakeServiceOptions().Url);
        Assert.Equal("http://localhost:5009", new HomeworkServiceOptions().Url);
    }

    [Fact]
    public void DownstreamServiceOptions_BindingUsesConfiguredUrl()
    {
        var config = Config(
            ("StudentService:Url", "http://10.0.0.5:5005"),
            ("MistakeService:Url", "http://10.0.0.7:5007"),
            ("HomeworkService:Url", "http://10.0.0.9:5009"));

        Assert.Equal("http://10.0.0.5:5005",
            config.GetSection(StudentServiceOptions.SectionName).Get<StudentServiceOptions>()!.Url);
        Assert.Equal("http://10.0.0.7:5007",
            config.GetSection(MistakeServiceOptions.SectionName).Get<MistakeServiceOptions>()!.Url);
        Assert.Equal("http://10.0.0.9:5009",
            config.GetSection(HomeworkServiceOptions.SectionName).Get<HomeworkServiceOptions>()!.Url);
    }

    [Fact]
    public void DownstreamServiceOptions_MissingSectionFallsBackToLocalhostDefaults()
    {
        // The exact binding shape Program.cs uses: an absent section yields null and the
        // constructor defaults replace the former inline ?? fallbacks.
        Assert.Null(Config().GetSection(StudentServiceOptions.SectionName).Get<StudentServiceOptions>());

        var student = Config(("StudentService:Other", "ignored")).GetSection(StudentServiceOptions.SectionName).Get<StudentServiceOptions>();
        Assert.NotNull(student);
        Assert.Equal("http://localhost:5005", student!.Url);
    }

    [Fact]
    public void OssAuditScheduleOptions_DefaultsMatchFormerInlineWorkerDefaults()
    {
        Assert.Equal(2, new OssAuditScheduleOptions().ScheduledHour);
        Assert.Equal(0, new OssAuditScheduleOptions().ScheduledMinute);
    }

    [Fact]
    public void OssAuditScheduleOptions_BindingOverridesDefaultsAndKeepsMissingKeys()
    {
        var full = Config(("OssAudit:ScheduledHour", "4"), ("OssAudit:ScheduledMinute", "30"))
            .GetSection(OssAuditScheduleOptions.SectionName).Get<OssAuditScheduleOptions>();
        Assert.NotNull(full);
        Assert.Equal(4, full!.ScheduledHour);
        Assert.Equal(30, full.ScheduledMinute);

        // A partial section keeps the property default for the missing key, matching the
        // former GetValue<int>(key, default) behavior for absent keys.
        var partial = Config(("OssAudit:ScheduledHour", "5"))
            .GetSection(OssAuditScheduleOptions.SectionName).Get<OssAuditScheduleOptions>();
        Assert.NotNull(partial);
        Assert.Equal(5, partial!.ScheduledHour);
        Assert.Equal(0, partial.ScheduledMinute);
    }

    [Fact]
    public void OssOptions_KeepsStructuralDefaultsAndCarriesNoCredentialDefault()
    {
        var options = new OssOptions();
        Assert.Equal("localhost:8333", options.InternalEndpoint);
        Assert.False(options.InternalSecure);
        Assert.Equal("ruoyu-study", options.BucketName);
        // Credentials have no code default: local mock values live only in
        // appsettings.Development.json and production values come from Consul KV or
        // environment variables; a missing credential fails at first use.
        Assert.Equal(string.Empty, options.AccessKey);
        Assert.Equal(string.Empty, options.SecretKey);
        Assert.Null(options.PublicBaseUrl);
    }

    [Fact]
    public void PortalProxyOptions_UrlDefaultsStayEmptySoUnconfiguredMeans503()
    {
        // Empty URL keeps the proxy "not configured -> 503" contract; these defaults must
        // NOT become localhost addresses, or unconfigured environments would silently
        // proxy to a wrong target (issue #117, scope item 9).
        Assert.Equal(string.Empty, new TeacherPortalOptions().Url);
        Assert.Equal(string.Empty, new AssistantPortalOptions().Url);
    }
}
