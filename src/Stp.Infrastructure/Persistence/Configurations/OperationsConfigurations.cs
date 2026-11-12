using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stp.Core.Domain;

namespace Stp.Infrastructure.Persistence.Configurations;

public sealed class BatchJobConfiguration : IEntityTypeConfiguration<BatchJob>
{
    public void Configure(EntityTypeBuilder<BatchJob> builder)
    {
        builder.ToTable("BatchJobs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.OptionsJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.OutputPath).HasMaxLength(1000);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAt);

        builder.HasMany(x => x.Items)
            .WithOne(x => x.Job)
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Logs)
            .WithOne(x => x.Job)
            .HasForeignKey(x => x.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BatchJobItemConfiguration : IEntityTypeConfiguration<BatchJobItem>
{
    public void Configure(EntityTypeBuilder<BatchJobItem> builder)
    {
        builder.ToTable("BatchJobItems");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.InputFileName).HasMaxLength(400).IsRequired();
        builder.Property(x => x.InputPath).HasMaxLength(1000);
        builder.Property(x => x.OutputPath).HasMaxLength(1000);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.WorkbookKind).HasConversion<int>();
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(x => x.JobId);
    }
}

public sealed class JobLogConfiguration : IEntityTypeConfiguration<JobLog>
{
    public void Configure(EntityTypeBuilder<JobLog> builder)
    {
        builder.ToTable("JobLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Level).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Details).HasColumnType("nvarchar(max)");

        builder.HasIndex(x => new { x.JobId, x.Timestamp });
    }
}

public sealed class MacroVersionConfiguration : IEntityTypeConfiguration<MacroVersion>
{
    public void Configure(EntityTypeBuilder<MacroVersion> builder)
    {
        builder.ToTable("MacroVersions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ChangeNote).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasMaxLength(200);

        builder.HasIndex(x => new { x.Name, x.VersionNumber }).IsUnique();
        builder.HasIndex(x => x.IsActive);

        builder.HasOne(x => x.ParentVersion)
            .WithMany()
            .HasForeignKey(x => x.ParentVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SyncSessionConfiguration : IEntityTypeConfiguration<SyncSession>
{
    public void Configure(EntityTypeBuilder<SyncSession> builder)
    {
        builder.ToTable("SyncSessions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceFileName).HasMaxLength(400).IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(x => x.UploadedAt);
        builder.HasIndex(x => x.Status);

        builder.HasMany(x => x.Changes)
            .WithOne(x => x.SyncSession)
            .HasForeignKey(x => x.SyncSessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SyncChangeConfiguration : IEntityTypeConfiguration<SyncChange>
{
    public void Configure(EntityTypeBuilder<SyncChange> builder)
    {
        builder.ToTable("SyncChanges");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityKey).HasMaxLength(400).IsRequired();
        builder.Property(x => x.ChangeType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.OldValueJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.NewValueJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.Resolution).HasMaxLength(500);

        builder.HasIndex(x => x.SyncSessionId);
        builder.HasIndex(x => x.IsConflict);
    }
}

public sealed class PrintPresetConfiguration : IEntityTypeConfiguration<PrintPreset>
{
    public void Configure(EntityTypeBuilder<PrintPreset> builder)
    {
        builder.ToTable("PrintPresets");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PaperSize).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Orientation).HasMaxLength(50).IsRequired();
        builder.Property(x => x.FontFamily).HasMaxLength(100).IsRequired();
        builder.Property(x => x.HeaderText).HasMaxLength(500);
        builder.Property(x => x.FooterText).HasMaxLength(500);
        builder.Property(x => x.Watermark).HasMaxLength(200);

        builder.HasIndex(x => x.Name).IsUnique();
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Actor).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100);
        builder.Property(x => x.EntityId).HasMaxLength(200);
        builder.Property(x => x.Details).HasColumnType("nvarchar(max)");

        builder.HasIndex(x => x.Timestamp);
    }
}

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(500).IsRequired();

        builder.HasIndex(x => x.CreatedAt);
    }
}

public sealed class StpUserConfiguration : IEntityTypeConfiguration<StpUser>
{
    public void Configure(EntityTypeBuilder<StpUser> builder)
    {
        builder.ToTable("StpUsers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Username).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(20).IsRequired();

        builder.HasIndex(x => x.Username).IsUnique();
        builder.HasIndex(x => x.IsActive);
    }
}

public sealed class SummaryDraftConfiguration : IEntityTypeConfiguration<SummaryDraft>
{
    public void Configure(EntityTypeBuilder<SummaryDraft> builder)
    {
        builder.ToTable("SummaryDrafts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(400).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();

        // Mỗi người giữ đúng 1 bản nháp cho 1 phiên — PUT là upsert.
        builder.HasIndex(x => new { x.SessionId, x.UserName }).IsUnique();
        builder.HasIndex(x => x.SavedAt);
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ForUser).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(400).IsRequired();
        builder.Property(x => x.Link).HasMaxLength(200);

        // Bell topbar: truy vấn unread của một người là nhịp nóng nhất.
        builder.HasIndex(x => new { x.ForUser, x.IsRead });
        builder.HasIndex(x => x.CreatedAt);
    }
}
