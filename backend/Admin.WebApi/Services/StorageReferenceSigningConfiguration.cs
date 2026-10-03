using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Admin.WebApi.Services;

public sealed class StorageReferenceSigningConfiguration : IDisposable
{
    public bool Enabled { get; }
    public string Issuer { get; }
    public string Audience { get; }
    public string Subject { get; }
    public string KeyId { get; }
    public RSA? Key { get; }
    public IReadOnlyDictionary<string, Uri> Providers { get; }

    public StorageReferenceSigningConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("StorageReferences");
        var enabled = section["Enabled"];
        if (enabled is not null && !bool.TryParse(enabled, out _)) throw Invalid();
        Enabled = enabled is not null && bool.Parse(enabled);
        Issuer = Text("Issuer", "urn:ruoyu:storage-audit");
        Audience = Text("Audience", "urn:ruoyu:storage-references");
        Subject = Text("Subject", "ruoyu.admin.storage-audit");
        KeyId = section["KeyId"] ?? "";
        var providers = new Dictionary<string, Uri>(StringComparer.Ordinal);
        foreach (var provider in new[] { "student", "mistake", "homework" })
        {
            var value = section[$"Providers:{provider}:BaseUrl"];
            if (value is null && !Enabled) continue;
            if (value is null || value.Any(char.IsWhiteSpace) || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" ||
                uri.Query.Length != 0 || uri.Fragment.Length != 0) throw Invalid();
            providers.Add(provider, uri);
        }
        Providers = providers;
        if (!Enabled) return;
        if (!Regex.IsMatch(KeyId, "^[A-Za-z0-9._-]{1,128}$")) throw Invalid();
        try
        {
            var path = section["PrivateKeyPath"] ?? "";
            if (!OperatingSystem.IsWindows() &&
                (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new CryptographicException();
            var pem = File.ReadAllText(path);
            if (!pem.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new CryptographicException();
            Key = RSA.Create(); Key.ImportFromPem(pem);
            if (Key.KeySize < 2048) throw new CryptographicException();
            _ = Key.ExportParameters(true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        { Key?.Dispose(); throw Invalid(); }
        string Text(string name, string fallback)
        {
            var text = section[name] ?? fallback;
            return string.IsNullOrWhiteSpace(text) || text.Any(char.IsWhiteSpace) ? throw Invalid() : text;
        }
    }
    public void Dispose() => Key?.Dispose();
    private static InvalidOperationException Invalid() => new("StorageReferences signing configuration is invalid or incomplete.");
}
