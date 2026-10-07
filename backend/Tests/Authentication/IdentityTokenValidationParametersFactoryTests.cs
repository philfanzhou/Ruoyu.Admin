using Microsoft.IdentityModel.Tokens;
using Ruoyu.Admin.Common.Authentication;
using Xunit;

namespace Admin.WebApi.Tests.Authentication;

public sealed class IdentityTokenValidationParametersFactoryTests
{
    private const string Primary = "https://explicit.example.test";
    private const string Migration = "https://migration.example.test";
    private const string Diagnostic = "Token issuer must match IdentityService:Issuer or IdentityService:AdditionalValidIssuers.";

    [Fact]
    public void Create_NormalizesAndFreezesOneReadOnlyTrustSnapshot()
    {
        var additional = new[] { "", " ", Primary, $" {Migration} ", Migration, Primary.ToUpperInvariant() };
        var options = new IdentityAuthenticationOptions { Issuer = $" {Primary} ", AdditionalValidIssuers = additional };
        var parameters = IdentityTokenValidationParametersFactory.Create(options);

        Assert.Equal(new[] { Primary, Migration, Primary.ToUpperInvariant() }, parameters.ValidIssuers);
        var exposed = Assert.IsAssignableFrom<IList<string>>(parameters.ValidIssuers);
        Assert.True(exposed.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => exposed[0] = "https://changed.example.test");
        options.Issuer = "https://changed.example.test";
        additional[3] = "https://later.example.test";
        parameters.ValidIssuer = options.Issuer;
        parameters.ValidIssuers = new[] { options.Issuer, additional[3] };

        Assert.Equal(Primary, Validate(parameters, Primary));
        Assert.Equal(Migration, Validate(parameters, Migration));
        AssertRejected(parameters, options.Issuer);
        AssertRejected(parameters, additional[3]);
        options.AdditionalValidIssuers = [additional[3]];
        var later = IdentityTokenValidationParametersFactory.Create(options);
        Assert.Equal(options.Issuer, Validate(later, options.Issuer));
        Assert.Equal(additional[3], Validate(later, additional[3]));
        AssertRejected(later, Migration);
    }

    [Theory]
    [InlineData("https://explicit.example.test/")]
    [InlineData("HTTPS://EXPLICIT.EXAMPLE.TEST")]
    [InlineData("https://issuer-canary.example.test")]
    [InlineData("")]
    [InlineData(null)]
    public void UnlistedIssuer_UsesOrdinalMatchAndFixedSafeException(string? issuer)
    {
        var parameters = IdentityTokenValidationParametersFactory.Create(new() { Issuer = Primary });
        AssertRejected(parameters, issuer);
    }

    [Fact]
    public void EmptyTrust_DoesNotFallBackToFrameworkIssuers()
    {
        var parameters = IdentityTokenValidationParametersFactory.Create(new() { Issuer = " ", AdditionalValidIssuers = null! });
        Assert.Empty(parameters.ValidIssuers!);
        parameters.ValidIssuer = Primary;
        parameters.ValidIssuers = new[] { Primary };
        AssertRejected(parameters, Primary);
    }

    [Fact]
    public async Task ConcurrentCreatesAndClones_KeepIndependentSnapshots()
    {
        var parameters = Enumerable.Range(0, 16).Select(i => IdentityTokenValidationParametersFactory.Create(new()
        { Issuer = $"https://issuer-{i}.example.test" })).ToArray();
        await Task.WhenAll(parameters.Select((original, i) => Task.Run(() =>
        {
            var clone = original.Clone();
            clone.ValidIssuers = new[] { "https://framework.example.test" };
            for (var repeat = 0; repeat < 32; repeat++)
            {
                Assert.Equal($"https://issuer-{i}.example.test", Validate(clone, $"https://issuer-{i}.example.test"));
                AssertRejected(clone, $"https://issuer-{(i + 1) % parameters.Length}.example.test");
                AssertRejected(clone, "https://framework.example.test");
            }
        })));
    }

    [Fact]
    public void Create_PreservesOtherValidationSettingsAndClaimTypes()
    {
        var keys = new SecurityKey[] { new SymmetricSecurityKey(new byte[32]) };
        var parameters = IdentityTokenValidationParametersFactory.Create(new()
        { Issuer = Primary, Audience = "test-audience", ClockSkewSeconds = 17 }, keys, "display", "roles");
        Assert.Same(keys, parameters.IssuerSigningKeys);
        Assert.True(parameters.ValidateIssuerSigningKey);
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.RequireExpirationTime);
        Assert.Equal("test-audience", parameters.ValidAudience);
        Assert.Equal(TimeSpan.FromSeconds(17), parameters.ClockSkew);
        Assert.Equal("display", parameters.NameClaimType);
        Assert.Equal("roles", parameters.RoleClaimType);
        var defaults = new TokenValidationParameters();
        var blank = IdentityTokenValidationParametersFactory.Create(new(), nameClaimType: " ", roleClaimType: "");
        Assert.Equal(defaults.NameClaimType, blank.NameClaimType);
        Assert.Equal(defaults.RoleClaimType, blank.RoleClaimType);
        Assert.Throws<ArgumentNullException>(() => IdentityTokenValidationParametersFactory.Create(null!));
    }

    private static string Validate(TokenValidationParameters parameters, string? issuer) =>
        parameters.IssuerValidator!(issuer!, new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "https://token-canary.example.test"), parameters);

    private static void AssertRejected(TokenValidationParameters parameters, string? issuer) =>
        Assert.Equal(Diagnostic, Assert.Throws<SecurityTokenInvalidIssuerException>(() => Validate(parameters, issuer)).Message);
}
