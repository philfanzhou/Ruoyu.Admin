namespace Admin.WebApi.Persistence;

public class OssAuditRun
{
    public long Id { get; set; }
    public long StartedAt { get; set; }
    public long? CompletedAt { get; set; }
    public int Status { get; set; }
    public int NewZombieCount { get; set; }
    public string TriggerType { get; set; } = "scheduled";
    public string? ErrorMessage { get; set; }
}
