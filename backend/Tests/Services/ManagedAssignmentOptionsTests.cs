using Admin.WebApi.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Admin.WebApi.Tests.Services;

public sealed class ManagedAssignmentOptionsTests
{
    private static IConfiguration Config(string? flag = null, string? origin = null, string? audience = "ADMIN", string? authority = "https://identity.example.test", string? trust = null)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Mistake:ManagedAssignmentEnabled"] = flag, ["MistakeService:Url"] = origin,
          ["IdentityService:Audience"] = audience, ["IdentityService:Authority"] = authority,
          ["IdentityService:RequireHttpsMetadata"] = trust }).Build();

    [Fact]
    public void AbsentOrFalsePreservesLegacyWithoutInventingManagedOrigin()
    {
        Assert.False(ManagedAssignmentOptions.Read(Config()).Enabled);
        Assert.False(ManagedAssignmentOptions.Read(Config("false")).Enabled);
        Assert.True(ManagedAssignmentOptions.Read(Config("true", "https://mistake.example.test")).Enabled);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public void InvalidBooleanFails(string flag) => Assert.Throws<InvalidOperationException>(() => ManagedAssignmentOptions.Read(Config(flag)));

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:5007")]
    [InlineData("https://user:password@mistake.example.test")]
    [InlineData("https://mistake.example.test/api")]
    [InlineData("https://mistake.example.test?redirect=other")]
    [InlineData("https://mistake.example.test#fragment")]
    public void EnabledRequiresExplicitSafeHttpsOrigin(string? origin) => Assert.Throws<InvalidOperationException>(() => ManagedAssignmentOptions.Read(Config("true", origin)));

    [Fact]
    public void EnabledRequiresIdentityTrust()
    {
        Assert.Throws<InvalidOperationException>(() => ManagedAssignmentOptions.Read(Config("true", "https://mistake.example.test", audience: "")));
        Assert.Throws<InvalidOperationException>(() => ManagedAssignmentOptions.Read(Config("true", "https://mistake.example.test", authority: "")));
        Assert.Throws<InvalidOperationException>(() => ManagedAssignmentOptions.Read(Config("true", "https://mistake.example.test", trust: "false")));
    }
}
