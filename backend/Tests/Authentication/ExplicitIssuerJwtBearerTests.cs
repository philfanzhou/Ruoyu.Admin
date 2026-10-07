using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Ruoyu.Admin.Common.Authentication;
using Xunit;

namespace Admin.WebApi.Tests.Authentication;

// This dedicated host exercises the real handler; it does not register schemes in Admin Program.
public sealed class ExplicitIssuerJwtBearerTests
{
    private const string Primary = "https://explicit.example.test";
    private const string Migration = "https://migration.example.test";
    private const string Authority = "https://discovery.example.test";
    private const string Audience = "test-audience";

    [Theory]
    [InlineData(Primary, "valid", HttpStatusCode.OK)]
    [InlineData(Migration, "valid", HttpStatusCode.OK)]
    [InlineData(Authority, "valid", HttpStatusCode.Unauthorized)]
    [InlineData("HTTPS://EXPLICIT.EXAMPLE.TEST", "valid", HttpStatusCode.Unauthorized)]
    [InlineData(Primary + "/", "valid", HttpStatusCode.Unauthorized)]
    [InlineData("https://untrusted.example.test", "valid", HttpStatusCode.Unauthorized)]
    [InlineData(Primary, "signature", HttpStatusCode.Unauthorized)]
    [InlineData(Primary, "audience", HttpStatusCode.Unauthorized)]
    [InlineData(Primary, "expired", HttpStatusCode.Unauthorized)]
    public async Task RealHandler_TrustsOnlyExplicitIssuersAndRetainsCryptographicChecks(
        string issuer, string fault, HttpStatusCode expected)
    {
        using var rsa = RSA.Create(2048);
        using var otherRsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "synthetic-key" };
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = key.KeyId };
        var metadata = new OpenIdConnectConfiguration { Issuer = Authority };
        metadata.SigningKeys.Add(key);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        Exception? failure = null;
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = Authority;
            options.MapInboundClaims = false;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            options.TokenValidationParameters = IdentityTokenValidationParametersFactory.Create(new()
            { Authority = Authority, Issuer = Primary, AdditionalValidIssuers = [Migration], Audience = Audience, ClockSkewSeconds = 0 },
                nameClaimType: "display", roleClaimType: "roles");
            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context => { failure = context.Exception; return Task.CompletedTask; }
            };
        });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/protected", (HttpContext context) => context.User.Identity!.Name == "synthetic-name"
            && context.User.IsInRole("synthetic-role") ? Results.Ok() : Results.StatusCode(500)).RequireAuthorization();
        await app.StartAsync();
        using var client = app.GetTestClient();
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(issuer, fault == "audience" ? "wrong-audience" : Audience,
            [new Claim("sub", "synthetic-subject"), new Claim("display", "synthetic-name"), new Claim("roles", "synthetic-role")],
            now.AddMinutes(-10), fault == "expired" ? now.AddMinutes(-1) : now.AddMinutes(2),
            new SigningCredentials(fault == "signature" ? otherKey : key, SecurityAlgorithms.RsaSha256));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK) Assert.Null(failure);
        else
        {
            Assert.NotNull(failure);
            if (fault == "valid")
            {
                // Framework wraps validation failures; the inner issuer diagnostic remains fixed and safe.
                var issuerFailure = Assert.IsType<SecurityTokenInvalidIssuerException>(
                    failure is AggregateException aggregate ? Assert.Single(aggregate.InnerExceptions) : failure);
                Assert.Equal("Token issuer must match IdentityService:Issuer or IdentityService:AdditionalValidIssuers.", issuerFailure.Message);
            }
        }
        await app.StopAsync();
    }
}
