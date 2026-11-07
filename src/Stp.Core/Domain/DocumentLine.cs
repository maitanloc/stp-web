namespace Stp.Core.Domain;

/// <summary>
/// Một dòng văn bản trong bảng mục lục, đúng theo các cột của sheet in Excel.
/// </summary>
public sealed class DocumentLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DossierId { get; set; }

    public Dossier Dossier { get; set; } = null!;

    public int Stt { get; set; } = 1;

    public string SoKyHieu { get; set; } = string.Empty;

    public string Ngay { get; set; } = string.Empty;

    public string TrichYeu { get; set; } = string.Empty;

    public string TacGia { get; set; } = string.Empty;

    public string ToSo { get; set; } = string.Empty;

    public string GhiChu { get; set; } = string.Empty;
}
