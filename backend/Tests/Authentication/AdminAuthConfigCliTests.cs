using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Admin.WebApi.Authentication;
using Xunit;

namespace Admin.WebApi.Tests.Authentication;

public sealed partial class AdminAuthConfigCliTests
{
    [Theory]
    [InlineData("https://localhost", "Production", 2)]
    [InlineData("https://LOCALHOST", "Testing", 2)]
    [InlineData("https://localhost.", "Development", 2)]
    [InlineData("https://admin.example.test", "Production", 0)]
    [InlineData("http://127.0.0.1:5020", "Development", 0)]
    [InlineData("http://[::1]:5020", "Testing", 0)]
    public async Task PreflightEnforcesHostnameTlsAndNumericLoopback(string origin, string environment, int exit)
    {
        using var fixture = new CliFixture();
        var config = AdminSessionDisabledTests.ValidConfiguration();
        config["IdentityService:Authority"] = origin;
        config["AdminOidc:RedirectUri"] = origin + "/api/auth/oidc/callback";
        config["AdminOidc:PostLogoutRedirectUri"] = origin + AdminOidcSettings.LogoutReturnPath;
        config["ConnectionStrings:AuditDb"] = "deliberately-unparsable-secret-canary";
        fixture.WriteSettings(config);
        var result = await fixture.Run(new() { ["ASPNETCORE_ENVIRONMENT"] = environment, ["DOTNET_ENVIRONMENT"] = environment });
        Assert.Equal(exit, result.Exit);
        Assert.Equal(exit == 0 ? "RUOYU_ADMIN_AUTH_CONFIG_VALID\n" : "", result.Out);
        Assert.Equal(exit == 0 ? "" : "RUOYU_ADMIN_AUTH_CONFIG_INVALID\n", result.Error);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "data")));
    }

    [Theory]
    [InlineData("http://192.168.55.10:5020", "Production", 0, new[] { "http://192.168.55.10:5020" })]
    [InlineData("http://10.1.2.3:5020", "Production", 0, new[] { "http://10.1.2.3:5020", "http://172.16.5.6:5002" })]
    [InlineData("http://[fd00::10]:5020", "Production", 0, new[] { "http://[FD00::10]:5020" })]
    [InlineData("http://192.168.55.10:5020", "Testing", 2, new[] { "http://192.168.55.10:5002" })]
    [InlineData("http://192.168.55.10:5020", "Production", 2, new[] { "http://192.168.55.11:5020" })]
    [InlineData("http://admin.example.test:5020", "Production", 2, new[] { "http://192.168.55.10:5020" })]
    [InlineData("http://127.0.0.1:5020", "Development", 0, new[] { "http://192.168.55.10:5020" })]
    [InlineData("http://127.0.0.1:5020", "Production", 2, new[] { "http://127.0.0.1:5020" })]
    public async Task PreflightAdmitsExplicitIntranetHttpOriginsByExactOriginOnly(string origin, string environment, int exit, string[] list)
    {
        using var fixture = new CliFixture();
        var config = AdminSessionDisabledTests.ValidConfiguration();
        config["IdentityService:Authority"] = origin;
        config["AdminOidc:RedirectUri"] = origin + "/api/auth/oidc/callback";
        config["AdminOidc:PostLogoutRedirectUri"] = origin + AdminOidcSettings.LogoutReturnPath;
        config["ConnectionStrings:AuditDb"] = "deliberately-unparsable-secret-canary";
        for (var index = 0; index < list.Length; index++) config["AdminOidc:IntranetHttpOrigins:" + index] = list[index];
        fixture.WriteSettings(config);
        var result = await fixture.Run(new() { ["ASPNETCORE_ENVIRONMENT"] = environment, ["DOTNET_ENVIRONMENT"] = environment });
        Assert.Equal(exit, result.Exit);
        Assert.Equal(exit == 0 ? "RUOYU_ADMIN_AUTH_CONFIG_VALID\n" : "", result.Out);
        Assert.Equal(exit == 0 ? "" : "RUOYU_ADMIN_AUTH_CONFIG_INVALID\n", result.Error);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "data")));
    }

    [Fact]
    public async Task PreflightAdmitsSplitIntranetAuthorityAndRedirectOrigins()
    {
        using var fixture = new CliFixture();
        var config = AdminSessionDisabledTests.ValidConfiguration();
        config["IdentityService:Authority"] = "http://192.168.55.10:5002";
        config["AdminOidc:RedirectUri"] = "http://192.168.55.10:5020/api/auth/oidc/callback";
        config["AdminOidc:PostLogoutRedirectUri"] = "http://192.168.55.10:5020" + AdminOidcSettings.LogoutReturnPath;
        config["AdminOidc:IntranetHttpOrigins:0"] = "http://192.168.55.10:5002";
        config["AdminOidc:IntranetHttpOrigins:1"] = "http://192.168.55.10:5020";
        config["ConnectionStrings:AuditDb"] = "deliberately-unparsable-secret-canary";
        fixture.WriteSettings(config);
        var result = await fixture.Run(); // Production by default.
        Assert.Equal(0, result.Exit);
        Assert.Equal("RUOYU_ADMIN_AUTH_CONFIG_VALID\n", result.Out);
        Assert.Equal("", result.Error);
    }

    public static TheoryData<string[]> MalformedIntranetLists() => new()
    {
        { new[] { "http://192.168.055.10:5020" } },
        { new[] { "http://[fe80::1]:5020" } },
        { new[] { "http://[::ffff:192.168.55.10]:5020" } },
        { new[] { "http://192.168.55.10:0" } },
        { new[] { "http://192.168.55.10:65536" } },
        { new[] { "http://192.168.55.10:5020/api" } },
        { new[] { "https://192.168.55.10:5020" } },
        { new[] { "http://8.8.8.8:5020" } },
        { new[] { "http://admin.intranet.test:5020" } },
        { new[] { "http://192.168.55.10" } },
        { new[] { " http://192.168.55.10:5020" } },
        { new[] { "http://user@192.168.55.10:5020" } },
        { new[] { "" } },
        { new[] { "http://192.168.55.10:5020", "http://192.168.55.10:5020" } },
        { new[] { "http://192.168.55.10:5020", "http://192.168.55.10:05020" } },
    };

    [Theory]
    [MemberData(nameof(MalformedIntranetLists))]
    public async Task PreflightRejectsMalformedIntranetHttpOriginListWithFixedOutputOnly(string[] list)
    {
        using var fixture = new CliFixture();
        var config = AdminSessionDisabledTests.ValidConfiguration();
        config["ConnectionStrings:AuditDb"] = "deliberately-unparsable-secret-canary";
        for (var index = 0; index < list.Length; index++) config["AdminOidc:IntranetHttpOrigins:" + index] = list[index];
        fixture.WriteSettings(config);
        var result = await fixture.Run();
        Assert.Equal(2, result.Exit);
        Assert.Equal("", result.Out);
        Assert.Equal("RUOYU_ADMIN_AUTH_CONFIG_INVALID\n", result.Error);
        foreach (var entry in list.Where(entry => entry.Length > 0))
            Assert.DoesNotContain(entry, result.Error); // only the fixed code, never a configured value
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "data")));
    }

    [Fact]
    public async Task PreflightRejectsScalarShapedIntranetHttpOriginList()
    {
        using var fixture = new CliFixture();
        var config = AdminSessionDisabledTests.ValidConfiguration();
        config["AdminOidc:IntranetHttpOrigins"] = "http://192.168.55.10:5020";
        config["ConnectionStrings:AuditDb"] = "deliberately-unparsable-secret-canary";
        fixture.WriteSettings(config);
        var result = await fixture.Run();
        Assert.Equal(2, result.Exit);
        Assert.Equal("", result.Out);
        Assert.Equal("RUOYU_ADMIN_AUTH_CONFIG_INVALID\n", result.Error);
    }

    [Theory]
    [InlineData(null, 0)][InlineData("true", 0)]
    [InlineData("false", 2)][InlineData("bad-secret-canary", 2)]
    public async Task PreflightExitsWithFixedOutputBeforeDatabaseWorkerOrListener(string? legacy, int exit)
    {
        using var fixture = new CliFixture();
        var config = AdminSessionDisabledTests.ValidConfiguration();
        config["ConnectionStrings:AuditDb"] = "deliberately-unparsable-secret-canary";
        if (legacy is not null) config["AdminOidc:Enabled"] = legacy;
        fixture.WriteSettings(config);
        var result = await fixture.Run();
        Assert.Equal(exit, result.Exit);
        Assert.Equal(exit == 0 ? "RUOYU_ADMIN_AUTH_CONFIG_VALID\n" : "", result.Out);
        Assert.Equal(exit == 0 ? "" : "RUOYU_ADMIN_AUTH_CONFIG_INVALID\n", result.Error);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "data")));
    }

    public static IEnumerable<object[]> JsonBooleanMigrationValues()
    {
        foreach (var key in new[] { "Enabled", "UseSessionForAdminApi", "UseSessionForLogout",
            "UseSessionForIdentityProxy", "UseSessionForPortalProxies" })
        foreach (var value in new[] { true, false })
            yield return ["AdminOidc:" + key, value];
    }

    [Theory]
    [MemberData(nameof(JsonBooleanMigrationValues))]
    public async Task ActualJsonBooleanTrueMigratesAndFalseRejectsBeforeHost(string key, bool value)
    {
        using var fixture = new CliFixture();
        var settings = AdminSessionDisabledTests.ValidConfiguration()
            .ToDictionary(entry => entry.Key, entry => (object?)entry.Value);
        settings["ConnectionStrings:AuditDb"] = "deliberately-unparsable-secret-canary";
        settings[key] = value;
        await File.WriteAllTextAsync(Path.Combine(fixture.Root, "appsettings.json"), JsonSerializer.Serialize(settings));
        var result = await fixture.Run();
        Assert.Equal(value ? 0 : 2, result.Exit);
        Assert.Equal(value ? "RUOYU_ADMIN_AUTH_CONFIG_VALID\n" : "", result.Out);
        Assert.Equal(value ? "" : "RUOYU_ADMIN_AUTH_CONFIG_INVALID\n", result.Error);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "data")));
    }

    [Fact]
    public async Task ConsulPrecedenceMatchesFormalStartupWhilePreflightNeverRefreshesCache()
    {
        using var fixture = new CliFixture();
        var file = AdminSessionDisabledTests.ValidConfiguration(); file["AdminOidc:Enabled"] = "false";
        fixture.WriteSettings(file);
        var cache = Path.Combine(fixture.Root, "cache"); Directory.CreateDirectory(cache);
        var cacheFile = Path.Combine(cache, "cache.json"); var metadata = Path.Combine(cache, "cache.metadata.json");
        await File.WriteAllTextAsync(cacheFile, "{\"AdminOidc:Enabled\":\"false\"}");
        await File.WriteAllTextAsync(metadata, "{\"canary\":\"unchanged\"}");
        var before = await File.ReadAllBytesAsync(cacheFile); var beforeMetadata = await File.ReadAllBytesAsync(metadata);
        using var consul = new ConsulFixture(new Dictionary<string, string?> { ["AdminOidc:Enabled"] = "true" });
        var env = new Dictionary<string, string?> { ["CONSUL_PORT"] = consul.Port.ToString(), ["CONSUL_CACHE_DIR"] = cache };
        Assert.Equal(0, (await fixture.Run(env)).Exit); // Consul overrides file.
        env["AdminOidc__Enabled"] = "false";
        Assert.Equal(2, (await fixture.Run(env)).Exit); // Environment overrides Consul.
        Assert.Equal(0, (await fixture.Run(env, ["--AdminOidc:Enabled", "true"])).Exit); // CLI overrides environment.
        Assert.Equal(before, await File.ReadAllBytesAsync(cacheFile));
        Assert.Equal(beforeMetadata, await File.ReadAllBytesAsync(metadata));
        Assert.Equal(new[] { "cache.json", "cache.metadata.json" }, Directory.GetFiles(cache).Select(Path.GetFileName).Order());
        var formal = await fixture.Run(env, validateOnly: false);
        Assert.NotEqual(0, formal.Exit);
        Assert.Contains("AdminOidc:Enabled", formal.Error);
        Assert.DoesNotContain("fixture-secret", formal.Error);
        // Formal startup retains its existing successful-Consul cache save contract.
        Assert.NotEqual(Encoding.UTF8.GetString(before), await File.ReadAllTextAsync(cacheFile));
        Assert.Contains("true", await File.ReadAllTextAsync(cacheFile));
    }

    [Fact]
    public async Task UnreachableConsulUsesExistingCacheWithoutWritingOrCreatingCacheDirectories()
    {
        using var fixture = new CliFixture();
        var file = AdminSessionDisabledTests.ValidConfiguration(); file["AdminOidc:Enabled"] = "false";
        fixture.WriteSettings(file);
        var cache = Path.Combine(fixture.Root, "cache"); Directory.CreateDirectory(cache);
        var path = Path.Combine(cache, "cache.json");
        await File.WriteAllTextAsync(path, "{\"AdminOidc:Enabled\":\"true\"}");
        var before = await File.ReadAllBytesAsync(path);
        var env = new Dictionary<string, string?> { ["CONSUL_CACHE_DIR"] = cache };
        Assert.Equal(0, (await fixture.Run(env)).Exit);
        Assert.Equal(before, await File.ReadAllBytesAsync(path)); Assert.Single(Directory.GetFiles(cache));
        var absent = Path.Combine(fixture.Root, "absent-cache"); env["CONSUL_CACHE_DIR"] = absent;
        Assert.Equal(2, (await fixture.Run(env)).Exit); Assert.False(Directory.Exists(absent));
    }

    private sealed class CliFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "admin-auth-cli-" + Guid.NewGuid().ToString("N"));
        internal CliFixture() => Directory.CreateDirectory(Root);
        internal void WriteSettings(Dictionary<string, string?> settings) => File.WriteAllText(Path.Combine(Root, "appsettings.json"), JsonSerializer.Serialize(settings));
        internal async Task<(int Exit, string Out, string Error)> Run(Dictionary<string, string?>? env = null, string[]? args = null, bool validateOnly = true)
        {
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            if (validateOnly) start.ArgumentList.Add("--validate-auth-config");
            start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(Root);
            foreach (var arg in args ?? []) start.ArgumentList.Add(arg);
            // The subprocess owns its environment: integration-fixture overrides cannot leak in.
            foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("CONSUL_", StringComparison.Ordinal)
                || key.StartsWith("AdminOidc__", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("IdentityService__", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            start.Environment["DOTNET_ENVIRONMENT"] = "Production";
            start.Environment["CONSUL_HOST"] = "127.0.0.1"; start.Environment["CONSUL_PORT"] = "1";
            start.Environment["CONSUL_TIMEOUT_MS"] = "500"; start.Environment["CONSUL_RETRY_COUNT"] = "1";
            foreach (var (key, value) in env ?? []) start.Environment[key] = value;
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
            catch { process.Kill(entireProcessTree: true); throw; }
            return (process.ExitCode, (await output).Replace("\r\n", "\n"), (await error).Replace("\r\n", "\n"));
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class ConsulFixture : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _server;
        internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        internal ConsulFixture(Dictionary<string, string?> snapshot)
        {
            var value = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot)));
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { new { Key = "config/ruoyu/auth", Value = value } }));
            _listener.Start();
            _server = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                    var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, _stop.Token); await stream.WriteAsync(body, _stop.Token);
                }
            });
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); _stop.Dispose(); }
    }
}
