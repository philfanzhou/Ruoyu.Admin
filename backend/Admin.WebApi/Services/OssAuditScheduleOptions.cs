namespace Admin.WebApi.Services;

/// <summary>
/// Local time-of-day schedule of the storage-audit worker (02:00 local time). These
/// properties are the single authoritative default; the <c>OssAudit</c> configuration
/// section only overrides them, and the worker re-reads the bound value on every cycle.
/// </summary>
public sealed class OssAuditScheduleOptions
{
    public const string SectionName = "OssAudit";

    public int ScheduledHour { get; set; } = 2;

    public int ScheduledMinute { get; set; } = 0;
}
