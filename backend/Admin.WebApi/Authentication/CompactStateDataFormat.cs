using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;

namespace Admin.WebApi.Authentication;

internal sealed class CompactStateDataFormat(TimeProvider time) : ISecureDataFormat<AuthenticationProperties>
{
    internal const int Capacity = 4096;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private readonly Dictionary<string, (AuthenticationProperties Properties, DateTimeOffset Expires)> _pending = [];
    internal int Count { get { lock (_gate) return _pending.Count; } }
    public string Protect(AuthenticationProperties data) => Protect(data, null);
    public string Protect(AuthenticationProperties data, string? purpose)
    {
        lock (_gate)
        {
            RemoveExpiredCore();
            if (_pending.Count >= Capacity) throw new InvalidOperationException("oidc.state_capacity");
            string key;
            do { key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)); } while (_pending.ContainsKey(key));
            _pending.Add(key, (new AuthenticationProperties(new Dictionary<string, string?>(data.Items)), time.GetUtcNow() + Lifetime));
            return key;
        }
    }
    public AuthenticationProperties? Unprotect(string? text) => Unprotect(text, null);
    public AuthenticationProperties? Unprotect(string? text, string? purpose)
    {
        if (text is null || text.Length != 43) return null;
        lock (_gate)
            return _pending.Remove(text, out var item) && item.Expires > time.GetUtcNow() ? item.Properties : null;
    }
    internal void RemoveExpired() { lock (_gate) RemoveExpiredCore(); }
    private void RemoveExpiredCore()
    {
        foreach (var key in _pending.Where(pair => pair.Value.Expires <= time.GetUtcNow()).Select(pair => pair.Key).ToArray())
            _pending.Remove(key);
    }
}
