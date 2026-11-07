namespace Stp.Core.Domain;

/// <summary>
/// Preset bố cục in. Mặc định là A4 dọc, mỗi hồ sơ một trang, giống bản in web.
/// </summary>
public sealed class PrintPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "A4 dọc chuẩn";

    public string PaperSize { get; set; } = "A4";

    public string Orientation { get; set; } = "Portrait";

    public double MarginLeft { get; set; } = 0.4;

    public double MarginRight { get; set; } = 0.4;

    public double MarginTop { get; set; } = 0.5;

    public double MarginBottom { get; set; } = 0.5;

    public double MarginHeader { get; set; } = 0.3;

    public double MarginFooter { get; set; } = 0.3;

    /// <summary>0 = dùng FitToPages; > 0 = scale phần trăm.</summary>
    public int Scale { get; set; } = 100;

    public bool FitToWidth { get; set; } = true;

    public bool FitToHeight { get; set; }

    public string FontFamily { get; set; } = "Times New Roman";

    public double FontSize { get; set; } = 10;

    public string? HeaderText { get; set; }

    public string? FooterText { get; set; }

    public string? Watermark { get; set; }

    public bool IsDefault { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
