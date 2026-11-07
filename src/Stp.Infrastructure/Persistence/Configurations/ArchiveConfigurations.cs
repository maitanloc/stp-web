using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stp.Core.Domain;

namespace Stp.Infrastructure.Persistence.Configurations;

public sealed class ArchiveWorkbookConfiguration : IEntityTypeConfiguration<ArchiveWorkbook>
{
    public void Configure(EntityTypeBuilder<ArchiveWorkbook> builder)
    {
        builder.ToTable("ArchiveWorkbooks");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SourceFileName).HasMaxLength(400).IsRequired();
        builder.Property(x => x.SourcePath).HasMaxLength(1000);
        builder.Property(x => x.FileHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Kind).HasConversion<int>();
        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.FileHash);
        builder.HasIndex(x => x.Name);

        builder.HasMany(x => x.Years)
            .WithOne(x => x.Workbook)
            .HasForeignKey(x => x.WorkbookId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ArchiveYearConfiguration : IEntityTypeConfiguration<ArchiveYear>
{
    public void Configure(EntityTypeBuilder<ArchiveYear> builder)
    {
        builder.ToTable("ArchiveYears");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SheetName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SourceSheets).HasMaxLength(1000);

        builder.HasIndex(x => new { x.WorkbookId, x.Year }).IsUnique();
        builder.HasIndex(x => x.Year);

        builder.HasMany(x => x.Dossiers)
            .WithOne(x => x.Year)
            .HasForeignKey(x => x.YearId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DossierConfiguration : IEntityTypeConfiguration<Dossier>
{
    public void Configure(EntityTypeBuilder<Dossier> builder)
    {
        builder.ToTable("Dossiers");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NumberLabel).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Group).HasMaxLength(300);
        builder.Property(x => x.Type).HasMaxLength(300);
        builder.Property(x => x.BoxNumber).HasMaxLength(100);
        builder.Property(x => x.SubNumber).HasMaxLength(100);
        builder.Property(x => x.OriginalSummary).HasMaxLength(2000);
        builder.Property(x => x.NumberNote).HasMaxLength(500);
        builder.Property(x => x.SourceVolume).HasMaxLength(100);
        builder.Property(x => x.FullText).HasColumnType("nvarchar(max)");
        builder.Property(x => x.Note).HasMaxLength(2000);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.WorkbookId, x.MlRow }).IsUnique();
        builder.HasIndex(x => new { x.YearId, x.Number });
        builder.HasIndex(x => x.Type);
        builder.HasIndex(x => x.UpdatedAt);

        // SQL Server không cho phép 2 đường cascade từ ArchiveWorkbook:
        // Workbook -> Year -> Dossier và Workbook -> Dossier.
        // Dossier bị xóa theo Year; quan hệ trực tiếp với Workbook là NoAction.
        builder.HasOne(x => x.Workbook)
            .WithMany()
            .HasForeignKey(x => x.WorkbookId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(x => x.Documents)
            .WithOne(x => x.Dossier)
            .HasForeignKey(x => x.DossierId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DocumentLineConfiguration : IEntityTypeConfiguration<DocumentLine>
{
    public void Configure(EntityTypeBuilder<DocumentLine> builder)
    {
        builder.ToTable("DocumentLines");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SoKyHieu).HasMaxLength(200);
        builder.Property(x => x.Ngay).HasMaxLength(100);
        builder.Property(x => x.TrichYeu).HasMaxLength(2000);
        builder.Property(x => x.TacGia).HasMaxLength(300);
        builder.Property(x => x.ToSo).HasMaxLength(100);
        builder.Property(x => x.GhiChu).HasMaxLength(500);

        builder.HasIndex(x => x.DossierId);
    }
}
