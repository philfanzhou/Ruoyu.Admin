namespace Admin.WebApi.Authentication;

internal sealed record MistakeSessionSettings(bool UseSessionToken, Uri? Origin)
{
    internal static MistakeSessionSettings Read(IConfiguration config, IHostEnvironment environment)
    {
        var section = config.GetSection("MistakeService:UseSessionToken");
        var exists = section.Exists() || config.AsEnumerable().Any(entry => string.Equals(entry.Key, section.Path, StringComparison.OrdinalIgnoreCase));
        var enabled = false;
        if (exists && !bool.TryParse(section.Value, out enabled)) throw new InvalidOperationException(section.Path);
        if (!enabled) return new(false, null);
        var value = config["MistakeService:Url"] ?? "";
        var dev = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        if (!AdminOidcSettings.IsSafeUri(value, dev, out var uri) || uri!.AbsolutePath != "/"
            || uri.AbsoluteUri.TrimEnd('/') != value.TrimEnd('/') || value.Contains('\\') || value.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("MistakeService:Url");
        return new(true, uri);
    }
    public override string ToString() => "MistakeSessionSettings";
}
