namespace Stp.Core.Domain;

/// <summary>
/// Loại workbook đầu vào được nhận diện từ cấu trúc sheet.
/// </summary>
public enum WorkbookKind
{
    Unknown = 0,

    /// <summary>Có ML + các sheet in (K1 = BANG_IN_HANG_LOAT), chỉ thiếu macro.</summary>
    StandardWithPrintSheets = 1,

    /// <summary>Có ML nhưng chưa có sheet in; hệ thống tự sinh sheet in.</summary>
    MlOnly = 2,

    /// <summary>Cấu trúc khác, cần map thủ công ở giai đoạn sau.</summary>
    Other = 99
}
