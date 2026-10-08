using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Admin.WebApi.Authentication;

/// <summary>
/// The explicit intranet HTTP deployment opt-in (<c>AdminOidc:IntranetHttpOrigins</c>): a strict
/// literal private-IP HTTP origin list. An entry is <c>http://</c> plus an RFC 1918 IPv4 literal
/// (10/8, 172.16/12, 192.168/16, no leading zeros) or a bracketed IPv6 Unique Local Address literal
/// (fc00::/7, IPv4-mapped addresses rejected), plus an explicit port 1-65535 — nothing else: no
/// user info, path, query, fragment, percent escapes, whitespace, domain names, loopback or public
/// addresses, and parsing never resolves a network name. The grammar and the canonical
/// <c>http://host:port</c> output spelling are byte-for-byte the rules SignaCore.Client.AspNetCore
/// 0.1.15 applies to <c>SignaCoreHostedLoginOptions.IntranetHttpOrigins</c>, so the same
/// configured list satisfies both this repository's gates and the package's own options
/// validation. A missing or empty list keeps the historical HTTPS-only enforcement.
/// </summary>
internal static class AdminIntranetHttpOrigins
{
    internal const string ConfigKey = "AdminOidc:IntranetHttpOrigins";

    /// <summary>
    /// Reads and canonicalizes the configured list. Every entry must canonicalize and no two
    /// entries may normalize to the same origin; any violation (or a value shape that cannot bind
    /// to a string list, e.g. a bare scalar) fails startup naming only the configuration key.
    /// </summary>
    internal static IReadOnlySet<string> Read(IConfiguration config)
    {
        var section = config.GetSection(ConfigKey);
        var values = section.Get<string[]>();
        if (values is null)
        {
            // The section exists but is not a string list (for example a bare scalar): a
            // malformed opt-in must fail loudly instead of silently falling back to HTTPS-only.
            if (section.Exists()) throw new InvalidOperationException(ConfigKey);
            return new HashSet<string>(StringComparer.Ordinal);
        }
        if (!TryResolve(values, out var origins)) throw new InvalidOperationException(ConfigKey);
        return origins;
    }

    /// <summary>
    /// Whether a parsed URI sits on one of the canonical intranet origins. The URI's own origin
    /// is rebuilt in the canonical spelling (bracketed IPv6, invariant port) so the comparison is
    /// exact: a nearby origin (another port, another host, or a non-HTTP scheme) never matches.
    /// </summary>
    internal static bool Contains(IReadOnlySet<string> origins, Uri uri)
    {
        if (uri.Scheme != "http") return false;
        var host = uri.HostNameType == UriHostNameType.IPv6 && !uri.Host.StartsWith('[')
            ? "[" + uri.Host + "]"
            : uri.Host;
        return origins.Contains("http://" + host + ":" + uri.Port.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Canonicalizes the whole configured list into one set; duplicates after
    /// normalization are a configuration error (the failure names the key, never a value).</summary>
    private static bool TryResolve(IEnumerable<string> values, out HashSet<string> origins)
    {
        origins = new(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!TryCanonicalize(value, out var origin) || !origins.Add(origin)) return false;
        }
        return true;
    }

    /// <summary>Canonicalizes one configured origin; the output is the normalized
    /// <c>http://host:port</c> spelling (lowercase IPv6, no IPv4 aliases).</summary>
    private static bool TryCanonicalize(string? value, out string origin)
    {
        origin = string.Empty;
        if (value is null || !value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return false;
        var authority = value[7..];
        if (authority.Length == 0 || authority.Any(character =>
                character is '/' or '\\' or '?' or '#' or '@' or '%' || char.IsWhiteSpace(character))) return false;
        string host;
        string portText;
        bool ipv6;
        if (authority[0] == '[')
        {
            var end = authority.IndexOf(']');
            if (end <= 1 || end + 1 >= authority.Length || authority[end + 1] != ':') return false;
            host = authority[1..end];
            portText = authority[(end + 2)..];
            ipv6 = true;
        }
        else
        {
            var separator = authority.IndexOf(':');
            if (separator <= 0) return false;
            host = authority[..separator];
            portText = authority[(separator + 1)..];
            ipv6 = false;
            var parts = host.Split('.');
            if (parts.Length != 4 || parts.Any(part => part.Length is 0 or > 3
                    || (part.Length > 1 && part[0] == '0') || !part.All(char.IsAsciiDigit))) return false;
        }
        if (portText.Length == 0 || !portText.All(char.IsAsciiDigit)
            || !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535 || !IPAddress.TryParse(host, out var address)) return false;
        var bytes = address.GetAddressBytes();
        if (ipv6)
        {
            if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.IsIPv4MappedToIPv6
                || (bytes[0] & 0xfe) != 0xfc) return false;
            host = "[" + address.ToString().ToLowerInvariant() + "]";
        }
        else
        {
            if (address.AddressFamily != AddressFamily.InterNetwork
                || !(bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                     || (bytes[0] == 192 && bytes[1] == 168))) return false;
            host = address.ToString();
        }
        origin = "http://" + host + ":" + port.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}
