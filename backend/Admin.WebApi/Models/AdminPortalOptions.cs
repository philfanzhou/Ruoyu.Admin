namespace Admin.WebApi;

/// <summary>
/// Configuration for admin_portal auth. The <see cref="AdminUserIds"/> whitelist lists the
/// Identity account IDs that are recognized as admins. When Identity calls admin_portal's
/// <c>/api/auth/callback</c> during token issuance, only these users receive the
/// <c>role:admin</c> claim.
/// </summary>
public sealed class AdminPortalOptions
{
    public const string SectionName = "AdminPortal";

    public List<string> AdminUserIds { get; set; } = new();
}
