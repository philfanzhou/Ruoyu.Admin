using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;

namespace Admin.WebApi.Authentication;

internal sealed class MemoryTicketStore(TimeProvider time) : ITicketStore
{
    internal const int Capacity = 4096;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private readonly object _gate = new();
    // Serialized snapshots prevent CookieHandler (or another request) mutating a stored ticket.
    private readonly Dictionary<string, (byte[] Ticket, DateTimeOffset Deadline)> _tickets = [];
    internal int Count { get { lock (_gate) return _tickets.Count; } }
    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);
    public Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext context, CancellationToken cancellationToken)
        => StoreAsync(ticket, cancellationToken);
    private Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            RemoveExpiredCore();
            if (_tickets.Count >= Capacity) throw new InvalidOperationException("oidc.session_capacity");
            var deadline = time.GetUtcNow() + Lifetime;
            if (ticket.Properties.ExpiresUtc is { } expires && expires < deadline) deadline = expires;
            ticket.Properties.ExpiresUtc = deadline;
            var snapshot = TicketSerializer.Default.Serialize(ticket);
            cancellationToken.ThrowIfCancellationRequested();
            var key = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            _tickets.Add(key, (snapshot, deadline));
            return Task.FromResult(key);
        }
    }
    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        lock (_gate)
        {
            if (!_tickets.TryGetValue(key, out var value)) return Task.FromResult<AuthenticationTicket?>(null);
            if (value.Deadline <= time.GetUtcNow())
            {
                _tickets.Remove(key);
                return Task.FromResult<AuthenticationTicket?>(null);
            }
            return Task.FromResult<AuthenticationTicket?>(TicketSerializer.Default.Deserialize(value.Ticket));
        }
    }
    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        lock (_gate)
        {
            if (_tickets.TryGetValue(key, out var value))
            {
                if (value.Deadline <= time.GetUtcNow()) _tickets.Remove(key);
                else
                {
                    // Absolute lifetime never extends, and Remove/Renew share the same lock.
                    ticket.Properties.ExpiresUtc = value.Deadline;
                    _tickets[key] = (TicketSerializer.Default.Serialize(ticket), value.Deadline);
                }
            }
        }
        return Task.CompletedTask;
    }
    // Removal and retrieval are one transition, including requests that cached Authenticate.
    internal AuthenticationTicket? Take(string key)
    {
        lock (_gate)
            return _tickets.Remove(key, out var value) && value.Deadline > time.GetUtcNow()
                ? TicketSerializer.Default.Deserialize(value.Ticket) : null;
    }
    public Task RemoveAsync(string key) { lock (_gate) _tickets.Remove(key); return Task.CompletedTask; }
    internal void RemoveExpired() { lock (_gate) RemoveExpiredCore(); }
    private void RemoveExpiredCore()
    {
        foreach (var key in _tickets.Where(pair => pair.Value.Deadline <= time.GetUtcNow()).Select(pair => pair.Key).ToArray())
            _tickets.Remove(key);
    }
}

internal sealed class OidcStoreCleanup(CompactStateDataFormat state, MemoryTicketStore tickets, AdminLogoutStateStore logout, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) { state.RemoveExpired(); tickets.RemoveExpired(); logout.RemoveExpired(); }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
