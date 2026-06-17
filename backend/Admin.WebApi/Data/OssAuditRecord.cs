namespace Admin.WebApi.Data;

public class OssAuditRecord
{
    public long Id { get; set; }
    public string ObjectPath { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public long Size { get; set; }
    public long LastModified { get; set; }
    public int Status { get; set; }
    public long CreatedAt { get; set; }
    public long? ResolvedAt { get; set; }
    public string? Note { get; set; }
}
