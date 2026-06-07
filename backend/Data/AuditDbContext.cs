using Microsoft.EntityFrameworkCore;

namespace Admin.WebApi.Data;

public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<OssAuditRecord> OssAuditRecords => Set<OssAuditRecord>();
    public DbSet<OssAuditRun> OssAuditRuns => Set<OssAuditRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OssAuditRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Bucket);
            entity.HasIndex(e => e.ObjectPath).IsUnique();
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<OssAuditRun>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StartedAt);
        });
    }
}

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

/// <summary>
/// 记录每次审计运行的状态
/// </summary>
public class OssAuditRun
{
    public long Id { get; set; }
    /// <summary>审计开始时间（Unix秒）</summary>
    public long StartedAt { get; set; }
    /// <summary>审计完成时间（Unix秒），null 表示仍在运行</summary>
    public long? CompletedAt { get; set; }
    /// <summary>运行状态：0=Running, 1=Completed, 2=Failed</summary>
    public int Status { get; set; }
    /// <summary>本次审计发现的新僵尸对象数量</summary>
    public int NewZombieCount { get; set; }
    /// <summary>触发方式：scheduled / manual</summary>
    public string TriggerType { get; set; } = "scheduled";
    /// <summary>错误信息（失败时）</summary>
    public string? ErrorMessage { get; set; }
}
