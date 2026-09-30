using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Admin.WebApi.Authentication;

internal sealed class AdminLogoutStateStore(TimeProvider time)
{
    internal const int Capacity = 4096;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Binding, DateTimeOffset Expires)> _pending = [];
    internal int Count { get { lock (_gate) return _pending.Count; } }
    internal static string RandomKey() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    internal static bool IsKey(string? value) => value is { Length: 43 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    internal string? Create(string binding)
    {
        lock (_gate)
        {
            RemoveExpiredCore();
            if (_pending.Count >= Capacity) return null;
            string state;
            do { state = RandomKey(); } while (_pending.ContainsKey(state));
            _pending.Add(state, (binding, time.GetUtcNow() + Lifetime));
            return state;
        }
    }
    internal bool Consume(string? state, string? binding)
    {
        if (!IsKey(state) || !IsKey(binding)) return false;
        lock (_gate)
        {
            if (!_pending.TryGetValue(state!, out var item)) return false;
            if (item.Expires <= time.GetUtcNow()) { _pending.Remove(state!); return false; }
            if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(binding!), System.Text.Encoding.ASCII.GetBytes(item.Binding))) return false;
            return _pending.Remove(state!);
        }
    }
    internal void Remove(string state) { lock (_gate) _pending.Remove(state); }
    internal void RemoveExpired() { lock (_gate) RemoveExpiredCore(); }
    private void RemoveExpiredCore()
    {
        foreach (var key in _pending.Where(p => p.Value.Expires <= time.GetUtcNow()).Select(p => p.Key).ToArray()) _pending.Remove(key);
    }
}
