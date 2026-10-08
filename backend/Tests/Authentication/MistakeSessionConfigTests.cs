using Admin.WebApi.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Admin.WebApi.Tests.Authentication;

public sealed partial class AdminAuthConfigCliTests
{
    [Theory]
    [InlineData(null, null, "Production", 0)]
    [InlineData("false", "http://lan.example.test:5007", "Production", 0)]
    [InlineData("true", "https://mistake.example.test", "Production", 0)]
    [InlineData("true", "http://127.0.0.1:5007", "Testing", 0)]
    [InlineData("true", "http://[::1]:5007/", "Development", 0)]
    [InlineData("yes", "https://mistake.example.test", "Production", 2)]
    [InlineData("", "https://mistake.example.test", "Production", 2)]
    [InlineData("true", null, "Testing", 2)]
    [InlineData("true", "http://127.0.0.1:5007", "Production", 2)]
    [InlineData("true", "http://192.168.55.10:5007", "Testing", 2)]
    [InlineData("true", "https://LOCALHOST.", "Testing", 2)]
    [InlineData("true", "https://user:secret-canary@mistake.example.test", "Production", 2)]
    [InlineData("true", "https://mistake.example.test/api", "Production", 2)]
    [InlineData("true", "https://mistake.example.test?secret-canary", "Production", 2)]
    [InlineData("true", "https://mistake.example.test#secret-canary", "Production", 2)]
    public async Task MistakePreflightUsesActualReadOnlyCommandBeforeHostAndCacheCreate(string? mode, string? url, string environment, int exit)
    {
        using var fixture = new CliFixture(); var config = AdminSessionDisabledTests.ValidConfiguration();
        if (mode is not null) config["MistakeService:UseSessionToken"] = mode;
        if (url is not null) config["MistakeService:Url"] = url;
        config["ConnectionStrings:AuditDb"] = "deliberately-unparsable-secret-canary"; fixture.WriteSettings(config);
        var cache = Path.Combine(fixture.Root, "must-not-create");
        var result = await fixture.Run(new() { ["ASPNETCORE_ENVIRONMENT"] = environment, ["DOTNET_ENVIRONMENT"] = environment, ["CONSUL_CACHE_DIR"] = cache });
        Assert.Equal(exit, result.Exit);
        Assert.Equal(exit == 0 ? "RUOYU_ADMIN_AUTH_CONFIG_VALID\n" : "", result.Out);
        Assert.Equal(exit == 0 ? "" : "RUOYU_ADMIN_AUTH_CONFIG_INVALID\n", result.Error);
        Assert.False(Directory.Exists(cache)); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "data")));
    }

    [Theory]
    [InlineData("true", "http://192.168.55.10:5007/", "Production", "http://192.168.55.10:5007", 0)]
    [InlineData("true", "http://[fd12::34]:5007/", "Production", "http://[FD12::34]:5007", 0)]
    [InlineData("true", "http://192.168.55.10:5007", "Testing", "http://192.168.55.10:5020", 2)]
    [InlineData("true", "http://192.168.55.10:5007", "Production", null, 2)]
    [InlineData("false", "http://192.168.55.10:5007", "Production", null, 0)]
    public async Task MistakePreflightAdmitsOnlyListedIntranetHttpOrigin(string? mode, string url, string environment, string? listed, int exit)
    {
        using var fixture = new CliFixture(); var config = AdminSessionDisabledTests.ValidConfiguration();
        config["MistakeService:UseSessionToken"] = mode;
        config["MistakeService:Url"] = url;
        if (listed is not null) config["AdminOidc:IntranetHttpOrigins:0"] = listed;
        config["ConnectionStrings:AuditDb"] = "deliberately-unparsable-secret-canary"; fixture.WriteSettings(config);
        var result = await fixture.Run(new() { ["ASPNETCORE_ENVIRONMENT"] = environment, ["DOTNET_ENVIRONMENT"] = environment });
        Assert.Equal(exit, result.Exit);
        Assert.Equal(exit == 0 ? "RUOYU_ADMIN_AUTH_CONFIG_VALID\n" : "", result.Out);
        Assert.Equal(exit == 0 ? "" : "RUOYU_ADMIN_AUTH_CONFIG_INVALID\n", result.Error);
    }
}

public sealed class MistakeSessionAllowlistTests
{
    [Theory]
    [InlineData("https://different.example.test/api/mistakes", "GET")]
    [InlineData("https://mistake.example.test:8443/api/mistakes", "GET")]
    [InlineData("http://mistake.example.test/api/mistakes", "GET")]
    [InlineData("https://mistake.example.test/api/students", "GET")]
    [InlineData("https://mistake.example.test/api/mistakes", "PATCH")]
    [InlineData("https://mistake.example.test/api/mistakes/upload", "GET")]
    [InlineData("https://mistake.example.test/api/mistakes/a%252Freview", "POST")]
    [InlineData("https://mistake.example.test/api/mistakes/%252e%252e", "GET")]
    [InlineData("https://mistake.example.test/api/mistakes/id/unknown", "POST")]
    [InlineData("https://mistake.example.test/api/mistakes/id#secret", "GET")]
    public void ExactOriginMethodAndPathRejectAlternateInputs(string uri, string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), uri);
        Assert.False(MistakeSessionHandler.IsAllowed(request, new Uri("https://mistake.example.test")));
    }
}
