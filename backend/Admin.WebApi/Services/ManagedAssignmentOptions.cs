namespace Admin.WebApi.Services;

public sealed record ManagedAssignmentOptions(bool Enabled)
{
    public static ManagedAssignmentOptions Read(IConfiguration configuration)
    {
        var raw = configuration["Mistake:ManagedAssignmentEnabled"];
        if (raw is not null && !bool.TryParse(raw, out _))
            throw new InvalidOperationException("Mistake:ManagedAssignmentEnabled must be a boolean.");
        var enabled = raw is not null && bool.Parse(raw);
        if (enabled)
        {
            var address = configuration["MistakeService:Url"];
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0
                || uri.Fragment.Length != 0 || address!.Any(char.IsControl) || address.Contains('*'))
                throw new InvalidOperationException("Managed assignment requires an explicit HTTPS MistakeService:Url origin.");
            if (string.IsNullOrWhiteSpace(configuration["IdentityService:Authority"])
                || string.IsNullOrWhiteSpace(configuration["IdentityService:Audience"])
                || !configuration.GetValue("IdentityService:RequireHttpsMetadata", true))
                throw new InvalidOperationException("Managed assignment requires configured identity trust and HTTPS metadata.");
        }
        return new(enabled);
    }
}
