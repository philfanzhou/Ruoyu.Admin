using Microsoft.EntityFrameworkCore;

namespace Admin.WebApi.Persistence;

public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<OssAuditRecord> OssAuditRecords => Set<OssAuditRecord>();
    public DbSet<OssAuditRun> OssAuditRuns => Set<OssAuditRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The model is the contract the InitialCreate baseline (and its executor's structure
        // verification) is generated from: it reproduces the retired inline DDL column by
        // column, including GENERATED ALWAYS AS IDENTITY, the store defaults, the PostgreSQL
        // default <table>_pkey primary-key constraint names, and the IX_* index names. Any
        // change here requires a new migration and an executor contract update.
        modelBuilder.Entity<OssAuditRecord>(entity =>
        {
            entity.ToTable("OssAuditRecords");
            entity.HasKey(e => e.Id).HasName("OssAuditRecords_pkey");
            entity.Property(e => e.Id).ValueGeneratedOnAdd().UseIdentityAlwaysColumn();
            entity.Property(e => e.ObjectPath).IsRequired();
            entity.Property(e => e.Bucket).IsRequired();
            entity.Property(e => e.Size).HasDefaultValue(0);
            entity.Property(e => e.LastModified).HasDefaultValue(0);
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValue(0);
            entity.HasIndex(e => e.ObjectPath).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Bucket);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<OssAuditRun>(entity =>
        {
            entity.ToTable("OssAuditRuns");
            entity.HasKey(e => e.Id).HasName("OssAuditRuns_pkey");
            entity.Property(e => e.Id).ValueGeneratedOnAdd().UseIdentityAlwaysColumn();
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.NewZombieCount).HasDefaultValue(0);
            entity.Property(e => e.TriggerType).HasDefaultValue("scheduled");
            entity.HasIndex(e => e.StartedAt);
        });
    }
}
