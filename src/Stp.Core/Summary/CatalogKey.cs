using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Stp.Core.Summary;

/// <summary>
/// Chuẩn hoá tên loại giấy tờ về khoá so sánh: bỏ dấu (gộp "đ"→"d"), chữ thường,
/// gộp khoảng trắng. Dùng chung giữa công cụ trích yếu và danh mục học được để
/// "đơn xi hộ khẩu" và "đơn xin hộ khẩu" luôn là cùng một mục.
/// </summary>
public static class CatalogKey
{
    public static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            // 'đ' không phân tách thành d + dấu nên phải quy đổi riêng, nếu không
            // "phiếu đề xuất" và "phieu de xuat" sẽ có khoá khác nhau.
            if (character is 'đ' or 'Đ')
            {
                builder.Append('d');
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>Chuẩn hoá cách hiển thị: gộp khoảng trắng, xoá rìa.</summary>
    public static string NormalizeDisplay(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");
}
