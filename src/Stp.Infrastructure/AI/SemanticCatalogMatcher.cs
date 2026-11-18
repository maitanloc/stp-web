using System.Text.RegularExpressions;

namespace Stp.Infrastructure.AI;

/// <summary>
/// Bộ so khớp ngữ nghĩa và âm vị học thích ứng (Adaptive Semantic & Phonetic Matcher).
/// Thay thế hoàn toàn các biểu thức chính quy (regex) cứng nhắc bằng thuật toán đo độ tương đồng
/// ngữ nghĩa, n-gram Jaccard, âm vần tiếng Việt và khoảng cách Levenshtein.
/// </summary>
public static class SemanticCatalogMatcher
{
    private static readonly char[] SpaceSeparators = new[] { ' ', ',', '.', ';', ':', '-', '\t', '\r', '\n' };

    /// <summary>
    /// Tìm kiếm và trích xuất danh sách giấy tờ từ văn bản nói dựa trên danh mục chuẩn (catalog).
    /// </summary>
    public static List<string> MatchItems(string spokenText, IEnumerable<string> catalog, double threshold = 0.58)
    {
        if (string.IsNullOrWhiteSpace(spokenText)) return new List<string>();

        var normalizedSpoken = NormalizeForMatching(spokenText);
        var catalogList = catalog.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        if (catalogList.Count == 0) return new List<string>();

        var matchedItems = new List<(string Item, double Score, int StartIndex)>();
        var spokenTokens = normalizedSpoken.Split(SpaceSeparators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var catalogItem in catalogList)
        {
            var normalizedCatalog = NormalizeForMatching(catalogItem);
            var catalogTokens = normalizedCatalog.Split(SpaceSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (catalogTokens.Length == 0) continue;

            // 1. Kiểm tra bao hàm trực tiếp (Exact substring containment)
            if (normalizedSpoken.Contains(normalizedCatalog, StringComparison.OrdinalIgnoreCase))
            {
                var idx = normalizedSpoken.IndexOf(normalizedCatalog, StringComparison.OrdinalIgnoreCase);
                matchedItems.Add((catalogItem, 1.0, idx));
                continue;
            }

            // 2. Trượt cửa sổ n-gram qua spokenTokens với kích thước quanh chiều dài catalogTokens
            var minWindow = Math.Max(1, catalogTokens.Length - 1);
            var maxWindow = Math.Min(spokenTokens.Length, catalogTokens.Length + 2);

            double bestScore = 0;
            int bestIdx = -1;

            for (int windowSize = minWindow; windowSize <= maxWindow; windowSize++)
            {
                for (int i = 0; i <= spokenTokens.Length - windowSize; i++)
                {
                    var windowPhrase = string.Join(" ", spokenTokens.Skip(i).Take(windowSize));
                    var score = CalculateSimilarity(windowPhrase, normalizedCatalog);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIdx = i;
                    }
                }
            }

            if (bestScore >= threshold)
            {
                matchedItems.Add((catalogItem, bestScore, bestIdx));
            }
        }

        // Sắp xếp theo thứ tự xuất hiện trong câu nói, loại bỏ trùng lặp
        return matchedItems
            .OrderBy(m => m.StartIndex)
            .ThenByDescending(m => m.Score)
            .Select(m => m.Item)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Tính toán độ tương đồng kết hợp giữa hai chuỗi ký tự (Hybrid N-Gram Jaccard + Levenshtein Similarity).
    /// Trả về điểm từ 0.0 (hoàn toàn khác) đến 1.0 (trùng khớp tuyệt đối).
    /// </summary>
    public static double CalculateSimilarity(string s1, string s2)
    {
        if (string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase)) return 1.0;
        if (string.IsNullOrWhiteSpace(s1) || string.IsNullOrWhiteSpace(s2)) return 0.0;

        var norm1 = NormalizeForMatching(s1);
        var norm2 = NormalizeForMatching(s2);
        if (norm1 == norm2) return 1.0;

        // Bigram Jaccard similarity (rất mạnh cho tiếng Việt có dấu và biến thể âm thanh)
        var bigrams1 = GetCharBigrams(norm1);
        var bigrams2 = GetCharBigrams(norm2);

        var intersection = bigrams1.Intersect(bigrams2).Count();
        var union = bigrams1.Union(bigrams2).Count();
        var jaccard = union == 0 ? 0.0 : (double)intersection / union;

        // Token Overlap similarity
        var tokens1 = norm1.Split(SpaceSeparators, StringSplitOptions.RemoveEmptyEntries);
        var tokens2 = norm2.Split(SpaceSeparators, StringSplitOptions.RemoveEmptyEntries);

        var tokenIntersection = tokens1.Intersect(tokens2, StringComparer.OrdinalIgnoreCase).Count();
        var maxTokenLen = Math.Max(tokens1.Length, tokens2.Length);
        var tokenOverlap = maxTokenLen == 0 ? 0.0 : (double)tokenIntersection / maxTokenLen;

        // Levenshtein similarity
        var levDist = ComputeLevenshteinDistance(norm1, norm2);
        var maxCharLen = Math.Max(norm1.Length, norm2.Length);
        var levSim = maxCharLen == 0 ? 1.0 : 1.0 - ((double)levDist / maxCharLen);

        // Trọng số phối hợp: 45% Jaccard + 35% Token Overlap + 20% Levenshtein
        return (jaccard * 0.45) + (tokenOverlap * 0.35) + (levSim * 0.20);
    }

    private static HashSet<string> GetCharBigrams(string text)
    {
        var bigrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < text.Length - 1; i++)
        {
            bigrams.Add(text.Substring(i, 2));
        }
        return bigrams;
    }

    private static int ComputeLevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;
        var d = new int[n + 1, m + 1];

        if (n == 0) return m;
        if (m == 0) return n;

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                var cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    /// <summary>
    /// Chuẩn hóa âm vị học tiếng Việt: loại bỏ dấu chấm câu, khoảng trắng thừa,
    /// đồng thời chuẩn hóa các cụm từ phổ biến mà bộ nhận dạng âm thanh (STT) hay nhầm lẫn.
    /// </summary>
    public static string NormalizeForMatching(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var lower = text.Trim().ToLowerInvariant();

        // 1. Loại bỏ các dấu câu gây nhiễu
        lower = Regex.Replace(lower, @"[.,;:?!/\-()\[\]""']", " ");

        // 2. Chuyển đổi một số biến thể âm vị học STT thường gặp trong văn phòng tư pháp
        // Nhầm âm / từ đồng nghĩa
        lower = Regex.Replace(lower, @"\bthức\s*hồn\b", "kết hôn");
        lower = Regex.Replace(lower, @"\bhôn\s*thú\b", "kết hôn");
        lower = Regex.Replace(lower, @"\bgiá\s*thú\b", "kết hôn");
        lower = Regex.Replace(lower, @"\bđộc\s*thân\b", "tình trạng hôn nhân");
        lower = Regex.Replace(lower, @"\bhộ\s*khẩu\s*gia\s*đình\b", "sổ hộ khẩu");
        lower = Regex.Replace(lower, @"\bchứng\s*minh\s*thư\b", "chứng minh nhân dân");
        lower = Regex.Replace(lower, @"\bcccd\b", "căn cước công dân");
        lower = Regex.Replace(lower, @"\bcmnd\b", "chứng minh nhân dân");
        lower = Regex.Replace(lower, @"\bkhai\s*sinh\s*bản\s*chính\b", "giấy khai sinh");
        lower = Regex.Replace(lower, @"\bbản\s*sao\s*khai\s*sinh\b", "bản sao trích lục khai sinh");

        // Rút gọn khoảng trắng
        return Regex.Replace(lower, @"\s+", " ").Trim();
    }
}
