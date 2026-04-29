namespace Admin.WebApi;

internal sealed class AdminCredentials
{
    public string AppId { get; }

    /// <summary>BCrypt hash of the plaintext secret from configuration.</summary>
    public string AppSecret { get; }

    public AdminCredentials(string appId, string appSecret)
    {
        AppId = appId;
        AppSecret = string.IsNullOrWhiteSpace(appSecret)
            ? string.Empty
            : BCrypt.Net.BCrypt.HashPassword(appSecret);
    }
}
