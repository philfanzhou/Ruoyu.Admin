using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Admin.WebApi.Tests.Integration;

// In-process HTTP authority: no production network, credentials, or signing key.
internal sealed class OidcTestAuthority : HttpMessageHandler
{
    internal const string Issuer = "https://identity.example.test";
    internal const string ClientId = "oidc-test-app";
    internal const string Secret = "fictitious-oidc-secret-canary";
    internal const string RedirectUri = "https://admin.example.test/api/auth/oidc/callback";
    private readonly RSA _rsa = RSA.Create(2048);
    // Serializes every segment of the /token branch that touches shared mutable state (the
    // _rsa signing instance, LastVerifier, LastIdToken, LastAccessToken) and the
    // same-family LegacyBearer signing. Concurrent RSA.SignData on ONE instance can
    // intermittently produce a silently INVALID signature (no exception, issue #63). The lock
    // deliberately does NOT cover the HoldToken infinite wait, the TokenFailure early
    // returns, or the missing-code 400: holding-, failure-, and cancellation-shaped tests
    // must keep their exact semantics, and the guarded section never awaits so the gate is
    // always released promptly.
    private readonly object _tokenGate = new();
    private readonly ConcurrentDictionary<string, (string Nonce, string Challenge, string Defect, string Subject, string AccessDefect)> _codes = [];
    internal readonly ConcurrentQueue<Dictionary<string, string>> TokenForms = [];
    internal readonly ConcurrentQueue<string?> AuthorizationHeaders = [];
    internal string? DiscoveryDefect { get; set; }
    internal string? TokenFailure { get; set; }
    internal bool HoldToken { get; set; }
    internal bool HoldDiscovery { get; set; }
    internal TaskCompletionSource DiscoveryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource TokenEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal string? LastIdToken { get; private set; }
    internal string? LastAccessToken { get; private set; }
    internal string? LastVerifier { get; private set; }
    internal int Redeems => TokenForms.Count;
    internal string Code(IDictionary<string, string> query, string defect = "valid", string subject = "fake-subject", string accessDefect = "valid")
    {
        var code = "fictitious-code-" + Guid.NewGuid().ToString("N");
        _codes[code] = (query["nonce"], query["code_challenge"], defect, subject, accessDefect);
        return code;
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath == "/.well-known/openid-configuration")
        {
            DiscoveryEntered.TrySetResult();
            if (HoldDiscovery) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (DiscoveryDefect == "500") return new(HttpStatusCode.InternalServerError);
            if (DiscoveryDefect == "json") return Json("invalid-json");
            if (DiscoveryDefect == "timeout") throw new TaskCanceledException("fake.timeout");
            return Json(JsonSerializer.Serialize(new
            {
                issuer = DiscoveryDefect == "issuer" ? Issuer + "/wrong" : Issuer,
                authorization_endpoint = Issuer + "/authorize", token_endpoint = Issuer + "/token", jwks_uri = Issuer + "/keys",
                response_types_supported = new[] { "code" }, subject_types_supported = new[] { "public" },
                id_token_signing_alg_values_supported = new[] { "RS256" }, scopes_supported = new[] { "openid", "profile" },
                code_challenge_methods_supported = new[] { "S256" }
            }));
        }
        if (request.RequestUri.AbsolutePath == "/keys")
        {
            if (DiscoveryDefect == "jwks") return Json("invalid-json");
            var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa) { KeyId = "test-kid" });
            return Json(JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid = "test-kid", use = "sig", alg = "RS256", n = key.N, e = key.E } } }));
        }
        if (request.RequestUri.AbsolutePath != "/token") throw new InvalidOperationException("fake.unexpected_endpoint");
        var raw = await request.Content!.ReadAsStringAsync(cancellationToken);
        var form = QueryHelpers.ParseQuery(raw).ToDictionary(p => p.Key, p => p.Value.ToString());
        TokenForms.Enqueue(form);
        AuthorizationHeaders.Enqueue(request.Headers.Authorization?.ToString());
        TokenEntered.TrySetResult();
        if (HoldToken) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (TokenFailure == "500") return new(HttpStatusCode.InternalServerError);
        if (TokenFailure == "json") return Json("invalid-json");
        if (TokenFailure == "timeout") throw new TaskCanceledException("fake.timeout");
        lock (_tokenGate)
        {
            if (!_codes.TryRemove(form["code"], out var handshake)) return new(HttpStatusCode.BadRequest);
            LastVerifier = form["code_verifier"];
            if (WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(LastVerifier))) != handshake.Challenge)
                throw new InvalidOperationException("fake.pkce_mismatch");
            LastIdToken = Mint(handshake.Nonce, handshake.Defect, handshake.Subject);
            LastAccessToken = MintAccess(handshake.Subject, handshake.AccessDefect);
            return Json(JsonSerializer.Serialize(new { access_token = LastAccessToken, token_type = "Bearer", expires_in = 900,
                id_token = LastIdToken, scope = "openid profile" }));
        }
    }
    internal SecurityKey SigningKey => new RsaSecurityKey(_rsa) { KeyId = "test-kid" };
    internal Task<HttpResponseMessage> DispatchAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => SendAsync(request, cancellationToken);
    internal string LegacyBearer()
    {
        var header = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"at+jwt\",\"kid\":\"test-kid\"}"));
        var payload = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { iss = Issuer, aud = "PlatformAudience", sub = "fake-user", role = "admin", exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() })));
        var input = header + "." + payload;
        lock (_tokenGate)
        {
            return input + "." + WebEncoders.Base64UrlEncode(_rsa.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
    }
    private string Mint(string nonce, string defect, string subject)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = JsonSerializer.Serialize(new { alg = defect == "unsigned" ? "none" : defect == "alg" ? "HS256" : "RS256",
            typ = defect == "typ" ? "at+jwt" : "JWT", kid = defect == "kid" ? "unknown-kid" : "test-kid" });
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = defect == "issuer" ? "https://wrong.example.test" : Issuer,
            ["aud"] = defect == "aud" ? "wrong-audience" : ClientId,
            ["sub"] = subject, ["iat"] = now, ["exp"] = now + 300,
            ["nonce"] = defect == "nonce" ? "wrong-nonce" : nonce,
            ["name"] = "fake-name", ["nickname"] = "fake-display", ["role"] = "admin"
        };
        if (defect == "sub-missing") payload.Remove("sub");
        if (defect == "sub-empty") payload["sub"] = "";
        if (defect == "sub-array") payload["sub"] = new[] { "one", "two" };
        if (defect == "iat-missing") payload.Remove("iat");
        if (defect == "iat-future") payload["iat"] = now + 120;
        if (defect == "iat-string") payload["iat"] = now.ToString();
        if (defect == "exp-before-iat") payload["exp"] = now - 1;
        if (defect == "exp-expired") { payload["iat"] = now - 600; payload["exp"] = now - 120; }
        if (defect == "nonce-missing") payload.Remove("nonce");
        if (defect == "display-missing") { payload.Remove("name"); payload.Remove("nickname"); }
        var raw = JsonSerializer.Serialize(payload);
        if (defect == "sub-duplicate") raw = raw.TrimEnd('}') + ",\"sub\":\"duplicate\"}";
        var input = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(header)) + "." + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw));
        if (defect == "unsigned") return input + ".";
        using var other = RSA.Create(2048);
        var signature = (defect == "signature" ? other : _rsa).SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return input + "." + WebEncoders.Base64UrlEncode(signature);
    }

    /// <summary>
    /// The SignaCore access token of the confidential code flow: an RS256 <c>at+jwt</c> signed
    /// by the authority key and correlated with the ID token's issuer and subject — exactly the
    /// shape the pre-sign-in authorization gate strictly validates before the whitelist runs.
    /// </summary>
    private string MintAccess(string subject, string defect)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = JsonSerializer.Serialize(new
        {
            alg = defect == "access-alg" ? "HS256" : "RS256",
            typ = defect == "access-typ" ? "JWT" : "at+jwt",
            kid = defect == "access-kid" ? "unknown-kid" : "test-kid"
        });
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = Issuer,
            ["aud"] = defect == "access-aud" ? "wrong-audience" : ClientId,
            ["sub"] = defect == "access-subject" ? "different-subject" : subject,
            ["role"] = "admin",
            ["iat"] = now, ["nbf"] = now - 10,
            ["exp"] = defect == "access-expired" ? now - 120 : now + 3600
        };
        if (defect == "access-opaque") return "fictitious-access-token-canary";
        var input = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(header)) + "." + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        using var other = RSA.Create(2048);
        var signature = (defect == "access-signature" ? other : _rsa).SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return input + "." + WebEncoders.Base64UrlEncode(signature);
    }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    protected override void Dispose(bool disposing) { if (disposing) _rsa.Dispose(); base.Dispose(disposing); }
}
