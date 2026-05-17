using Microsoft.EntityFrameworkCore;

namespace Admin.WebApi.Data;

public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<OssAuditRecord> OssAuditRecords => Set<OssAuditRecord>();

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
