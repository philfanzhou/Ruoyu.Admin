using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ruoyu.Admin.ServiceClients.StorageReferences;

public static class StorageReferenceContract
{
    public const string Version = "storage-references-v1";
    public const string Route = "/api/integrations/storage-audit/references/snapshots";
    public const int PageSize = 500;
    public const int MaximumEntries = 100000;
    public const int MaximumBytes = 32 * 1024 * 1024;
    public static readonly UTF8Encoding Utf8 = new(false, true);

    public static string Coverage(string provider) => provider switch
    {
        "student" => "current,fingerprint,acquired,archived,handoff-receipt",
        "mistake" => "item,practice,intake,target-object,target-attempt,return,return-object,return-attempt,handoff",
        "homework" => "detail,daily-submission,annotation",
        _ => throw new ArgumentException("Unknown reference provider.", nameof(provider))
    };

    public static string[] CanonicalKeys(IEnumerable<string> paths)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        long byteCount = 0;
        foreach (var path in paths)
        {
            ValidateKey(path);
            if (!keys.Add(path)) continue;
            byteCount += 4L + Utf8.GetByteCount(path);
            if (keys.Count > MaximumEntries || byteCount > MaximumBytes)
                throw new InvalidOperationException("Reference snapshot limit exceeded.");
        }
        // Encode each key once; a maximum-sized snapshot must not allocate UTF-8 buffers
        // for every comparison while validating each immutable page.
        var encoded = keys.Select(key => (Key: key, Bytes: Utf8.GetBytes(key))).ToArray();
        Array.Sort(encoded, (left, right) => left.Bytes.AsSpan().SequenceCompareTo(right.Bytes));
        return encoded.Select(entry => entry.Key).ToArray();
    }

    public static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key)) throw new InvalidOperationException("Invalid reference key.");
        _ = Utf8.GetByteCount(key);
    }

    public static int CompareKeys(string left, string right) =>
        Utf8.GetBytes(left).AsSpan().SequenceCompareTo(Utf8.GetBytes(right));

    public static string Digest(string provider, IReadOnlyList<string> keys)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        Append(Version); Append(provider); Append(keys.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var key in keys) { ValidateKey(key); Append(key); }
        if (keys.Count > MaximumEntries) throw new InvalidOperationException("Reference snapshot limit exceeded.");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        void Append(string value)
        {
            var bytes = Utf8.GetBytes(value);
            size += 4L + bytes.Length;
            if (size > MaximumBytes) throw new InvalidOperationException("Reference snapshot limit exceeded.");
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length); hash.AppendData(bytes);
        }
    }
}
