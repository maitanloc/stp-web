using Microsoft.EntityFrameworkCore;
using Stp.Core.Domain;

namespace Stp.Infrastructure.Persistence;

/// <summary>
/// DbContext chính cho database StpArchive trên SQL Server.
/// Toàn bộ cấu hình bảng nằm trong thư mục <c>Configurations</c>.
/// </summary>
public sealed class StpDbContext : DbContext
{
    public StpDbContext(DbContextOptions<StpDbContext> options)
        : base(options)
    {
    }

    public DbSet<ArchiveWorkbook> ArchiveWorkbooks => Set<ArchiveWorkbook>();

    public DbSet<ArchiveYear> ArchiveYears => Set<ArchiveYear>();

    public DbSet<Dossier> Dossiers => Set<Dossier>();

    public DbSet<DocumentLine> DocumentLines => Set<DocumentLine>();

    public DbSet<PrintPreset> PrintPresets => Set<PrintPreset>();

    public DbSet<BatchJob> BatchJobs => Set<BatchJob>();

    public DbSet<BatchJobItem> BatchJobItems => Set<BatchJobItem>();

    public DbSet<JobLog> JobLogs => Set<JobLog>();

    public DbSet<MacroVersion> MacroVersions => Set<MacroVersion>();

    public DbSet<SyncSession> SyncSessions => Set<SyncSession>();

    public DbSet<SyncChange> SyncChanges => Set<SyncChange>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StpDbContext).Assembly);
    }
}
