namespace Admin.WebApi.Persistence;

/// <summary>
/// One database-hosted application setting override (issue #118). The primary key is the
/// registered setting key (for example <c>OssAudit:ScheduledHour</c>); <see cref="Value"/>
/// holds the registry-normalized value, and <see cref="UpdatedBy"/> records the admin
/// subject from the server session ticket that last wrote the row. The presence of a row
/// is itself the override: deleting it falls resolution back to static configuration or
/// the options-class default.
/// </summary>
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public long UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
