namespace Stp.Core.Domain;

/// <summary>
/// Ghi chú/comment theo dòng trên bảng sống kho hồ sơ (Trụ D — J2), kiểu
/// comment Google Sheets: "hồ sơ này thiếu số tờ, nhờ Lộc check". Gắn đúng
/// một hồ sơ, ai trong nhóm đọc được ngay qua hub realtime.
/// </summary>
public sealed class DossierComment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DossierId { get; set; }

    public Dossier Dossier { get; set; } = null!;

    /// <summary>Tên người gửi (tự khai hoặc tài khoản nhóm, tối đa 100 ký tự).</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>Nội dung ghi chú dạng văn bản thuần (tối đa 1000 ký tự).</summary>
    public string Body { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
