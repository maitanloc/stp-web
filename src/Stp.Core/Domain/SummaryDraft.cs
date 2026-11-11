namespace Stp.Core.Domain;

/// <summary>
/// Bản nháp "Bổ sung trích yếu" sao lưu lên server theo phiên + người dùng —
/// F5, mất localStorage hay đổi máy vẫn khôi phục được phần sửa chưa xuất.
/// PayloadJson là JSON nguyên trạng phía client gửi (trích yếu, số tờ, giấy
/// tờ từng dòng) nên server không cần biết cấu trúc chi tiết.
/// </summary>
public sealed class SummaryDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Phiên làm việc (Guid) — cùng khoá với API summary tool.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Người sở hữu bản nháp (tên hiển thị nhóm, tối đa 100 ký tự).</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Tên file gốc để hiển thị khi khôi phục.</summary>
    public string FileName { get; set; } = string.Empty;

    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Nội dung bản nháp — JSON nguyên trạng từ client (nvarchar(max)).</summary>
    public string PayloadJson { get; set; } = string.Empty;
}
