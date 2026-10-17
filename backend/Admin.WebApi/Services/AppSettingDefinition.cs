using System.Globalization;

namespace Admin.WebApi.Services;

/// <summary>
/// One registered database-hosted application setting: its key, value domain, and the
/// normalized default inherited from the options class that owns the setting. Keys without
/// a definition here are never read from or written to the <c>AppSettings</c> table. The
/// v1 value type is a range-constrained integer; the normalized form is the invariant
/// decimal string (for example <c>"2"</c>).
/// </summary>
public sealed class AppSettingDefinition
{
    public AppSettingDefinition(string key, string description, int minInclusive, int maxInclusive, int defaultValue)
    {
        if (minInclusive > maxInclusive)
            throw new ArgumentOutOfRangeException(nameof(minInclusive));
        Key = key;
        Description = description;
        MinInclusive = minInclusive;
        MaxInclusive = maxInclusive;
        DefaultValue = defaultValue.ToString(CultureInfo.InvariantCulture);
    }

    public string Key { get; }

    public string Description { get; }

    public int MinInclusive { get; }

    public int MaxInclusive { get; }

    public string DefaultValue { get; }

    /// <summary>
    /// Parses and validates a raw value against the value domain, producing the normalized
    /// stored/returned form. Surrounding whitespace is tolerated; everything else
    /// (non-integers, fractions, out-of-range values) is rejected.
    /// </summary>
    public bool TryNormalize(string? rawValue, out string normalized)
    {
        normalized = string.Empty;
        if (rawValue is null)
            return false;
        if (!int.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return false;
        if (value < MinInclusive || value > MaxInclusive)
            return false;
        normalized = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    /// The fixed validation-failure message: it names the key and its constraint and never
    /// echoes the rejected value.
    /// </summary>
    public string InvalidMessage() =>
        $"Setting '{Key}' must be an integer between {MinInclusive} and {MaxInclusive}.";
}
