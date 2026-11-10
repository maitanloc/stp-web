using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stp.Core.Domain;

namespace Stp.Infrastructure.Persistence.Configurations;

/// <summary>
/// Bảng CatalogItems — "danh mục học được": loại giấy tờ người dùng thêm tự lưu
/// bền, các phiên sau không phải gõ lại. Unique theo khoá chuẩn hoá để biến thể
/// ("đơn xi hộ khẩu" ~ "đơn xin hộ khẩu") luôn gộp về một dòng.
/// </summary>
public sealed class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("CatalogItems");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NormalizedKey).HasMaxLength(300).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DossierTypeHint).HasMaxLength(100);

        builder.HasIndex(x => x.NormalizedKey).IsUnique();
        builder.HasIndex(x => x.IsDisabled);
        builder.HasIndex(x => new { x.IsDisabled, x.UsageCount });
    }
}
