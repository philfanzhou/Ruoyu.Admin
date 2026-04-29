using FluentAssertions;
using Xunit;

namespace Admin.WebApi.Tests;

public class AdminCredentialsTests
{
    [Fact]
    public void Constructor_WithValidParameters_ShouldSetAppId()
    {
        var appId = "test-app-id";
        var appSecret = "test-secret";

        var credentials = new AdminCredentials(appId, appSecret);

        credentials.AppId.Should().Be(appId);
    }

    [Fact]
    public void Constructor_WithValidSecret_ShouldHashSecret()
    {
        var appId = "test-app-id";
        var appSecret = "test-secret";

        var credentials = new AdminCredentials(appId, appSecret);

        credentials.AppSecret.Should().NotBe(appSecret);
        credentials.AppSecret.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Constructor_WithNullSecret_ShouldSetEmptySecret()
    {
        var appId = "test-app-id";

        var credentials = new AdminCredentials(appId, null!);

        credentials.AppSecret.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithEmptySecret_ShouldSetEmptySecret()
    {
        var appId = "test-app-id";

        var credentials = new AdminCredentials(appId, string.Empty);

        credentials.AppSecret.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithWhitespaceSecret_ShouldSetEmptySecret()
    {
        var appId = "test-app-id";

        var credentials = new AdminCredentials(appId, "   ");

        credentials.AppSecret.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithSameSecrets_ShouldProduceDifferentHashes()
    {
        var appId = "test-app-id";
        var secret = "my-secret";

        var credentials1 = new AdminCredentials(appId, secret);
        var credentials2 = new AdminCredentials(appId, secret);

        credentials1.AppSecret.Should().NotBe(credentials2.AppSecret);
    }

    [Fact]
    public void Constructor_WithNullAppId_ShouldSetNullAppId()
    {
        var secret = "test-secret";

        var credentials = new AdminCredentials(null!, secret);

        credentials.AppId.Should().BeNull();
    }
}
