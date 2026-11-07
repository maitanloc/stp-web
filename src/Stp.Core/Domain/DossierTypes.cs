using System.Text.RegularExpressions;

namespace Stp.Core.Domain;

/// <summary>Thứ tự hiển thị thống nhất của 8 loại hồ sơ trong workbook.</summary>
public static class DossierTypes
{
    public static readonly string[] Preferred =
    {
        "Đăng ký kết hôn",
        "Thay đổi tên & khai sinh",
        "Đăng ký nuôi con nuôi",
        "Con ngoài giá thú",
        "Đăng ký khai sinh",
        "Đăng ký khai tử",
        "Xác nhận Quốc tịch",
        "Ghi chú Kết hôn"
    };

    public static string Normalize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return string.Empty;
        }

        return Regex.Replace(type.Trim(), @"\s+", " ");
    }

    public static int Order(string? type)
    {
        var normalized = Normalize(type);
        if (normalized.Length == 0)
        {
            return int.MaxValue;
        }

        for (var i = 0; i < Preferred.Length; i++)
        {
            if (string.Equals(Preferred[i], normalized, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return Preferred.Length + 1;
    }
}
