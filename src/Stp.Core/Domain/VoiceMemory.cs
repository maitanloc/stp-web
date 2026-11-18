namespace Stp.Core.Domain;

/// <summary>
/// Lưu trữ ký ức và mẫu câu tương tác giọng nói thực tế của người dùng để hệ thống học tập tự động (Dynamic Few-Shot Learning).
/// Giúp AI ngày càng hiểu sâu ngữ cảnh của phòng mà không cần hardcode luật cứng.
/// </summary>
public sealed class VoiceMemory
{
    public long Id { get; set; }

    /// <summary>Câu nói gốc ghi nhận từ micro / speech-to-text.</summary>
    public string RawSpeech { get; set; } = string.Empty;

    /// <summary>Câu nói đã chuẩn hóa (lowercase, xóa khoảng trắng thừa) để so khớp.</summary>
    public string CleanSpeech { get; set; } = string.Empty;

    /// <summary>Lệnh hành động đã chốt (ví dụ: apply_missing, apply_fast, fill, filter...).</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Danh sách giấy tờ trích xuất (JSON mảng string, ví dụ: ["Giấy khai sinh", "Căn cước công dân"]).</summary>
    public string ItemsJson { get; set; } = "[]";

    /// <summary>Số tờ nếu có.</summary>
    public string? SheetNumber { get; set; }

    /// <summary>Dòng chỉ định nếu có.</summary>
    public int? TargetRowNumber { get; set; }

    /// <summary>Hồ sơ số nếu có.</summary>
    public string? TargetDocumentNumber { get; set; }

    /// <summary>Người nói / người xác nhận.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>Tần suất câu lệnh này được lặp lại và xác nhận thành công.</summary>
    public int UsageCount { get; set; } = 1;

    /// <summary>Thời điểm ghi nhận lần đầu.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Thời điểm dùng gần nhất.</summary>
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;
}
