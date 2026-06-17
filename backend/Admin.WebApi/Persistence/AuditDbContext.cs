using Microsoft.EntityFrameworkCore;

namespace Admin.WebApi.Persistence;

public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<OssAuditRecord> OssAuditRecords => Set<OssAuditRecord>();
    public DbSet<OssAuditRun> OssAuditRuns => Set<OssAuditRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OssAuditRecord>(entity =>
        {
            entity.ToTable("OssAuditRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.ObjectPath).IsRequired();
            entity.Property(e => e.Bucket).IsRequired();
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.HasIndex(e => e.ObjectPath).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Bucket);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<OssAuditRun>(entity =>
        {
            entity.ToTable("OssAuditRuns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.NewZombieCount).HasDefaultValue(0);
            entity.Property(e => e.TriggerType).HasDefaultValue("scheduled");
            entity.HasIndex(e => e.StartedAt);
        });
    }
}
