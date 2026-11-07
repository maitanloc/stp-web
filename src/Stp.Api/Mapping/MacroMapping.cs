using Stp.Contracts;
using Stp.Core.Macros;

namespace Stp.Api.Mapping;

/// <summary>Chuyển model macro sang DTO và tính diff theo dòng.</summary>
internal static class MacroMapping
{
    public static MacroVersionSummaryResponse ToSummary(this MacroVersionSummary version) =>
        new(
            version.Id,
            version.Name,
            version.VersionNumber,
            version.CodeHash,
            version.ChangeNote,
            version.CreatedBy,
            version.CreatedAt,
            version.IsActive,
            version.ParentVersionId,
            version.LineCount);

    public static MacroVersionDetailResponse ToResponse(this MacroVersionDetail version) =>
        new(
            version.Id,
            version.Name,
            version.VersionNumber,
            version.CodeHash,
            version.ChangeNote,
            version.CreatedBy,
            version.CreatedAt,
            version.IsActive,
            version.ParentVersionId,
            version.LineCount,
            version.Code);

    public static MacroValidationResponse ToResponse(this MacroValidationResult result) =>
        new(result.IsValid, result.Errors, result.Warnings);

    /// <summary>Diff theo dòng bằng LCS; đủ nhanh cho file macro vài nghìn dòng.</summary>
    public static MacroDiffResponse Diff(
        Guid newVersionId,
        Guid? oldVersionId,
        string newCode,
        string? oldCode)
    {
        const int MaxLinesForLcs = 1500;

        var newLines = SplitLines(newCode);
        var oldLines = SplitLines(oldCode ?? string.Empty);

        var added = 0;
        var removed = 0;
        var unchanged = 0;
        var output = new List<MacroDiffLineResponse>();

        if (oldLines.Length + newLines.Length > MaxLinesForLcs)
        {
            // File quá lớn: so sánh tuyến tính, tránh cấp phát bảng LCS lớn.
            AddNaiveDiff(output, oldLines, newLines, ref added, ref removed, ref unchanged);
        }
        else
        {
            AddLcsDiff(output, oldLines, newLines, ref added, ref removed, ref unchanged);
        }

        return new MacroDiffResponse(
            newVersionId,
            oldVersionId,
            added,
            removed,
            unchanged,
            output);
    }

    private static string[] SplitLines(string code) =>
        code.ReplaceLineEndings("\n").Split('\n');

    private static void AddLcsDiff(
        List<MacroDiffLineResponse> output,
        string[] oldLines,
        string[] newLines,
        ref int added,
        ref int removed,
        ref int unchanged)
    {
        var rows = oldLines.Length;
        var columns = newLines.Length;
        var table = new int[rows + 1, columns + 1];

        for (var row = rows - 1; row >= 0; row--)
        {
            for (var column = columns - 1; column >= 0; column--)
            {
                table[row, column] = string.Equals(
                    oldLines[row],
                    newLines[column],
                    StringComparison.Ordinal)
                    ? table[row + 1, column + 1] + 1
                    : Math.Max(table[row + 1, column], table[row, column + 1]);
            }
        }

        var oldIndex = 0;
        var newIndex = 0;
        while (oldIndex < rows && newIndex < columns)
        {
            if (string.Equals(oldLines[oldIndex], newLines[newIndex], StringComparison.Ordinal))
            {
                output.Add(new MacroDiffLineResponse(
                    "same",
                    oldIndex + 1,
                    newIndex + 1,
                    newLines[newIndex]));
                unchanged++;
                oldIndex++;
                newIndex++;
            }
            else if (table[oldIndex + 1, newIndex] >= table[oldIndex, newIndex + 1])
            {
                output.Add(new MacroDiffLineResponse(
                    "removed",
                    oldIndex + 1,
                    null,
                    oldLines[oldIndex]));
                removed++;
                oldIndex++;
            }
            else
            {
                output.Add(new MacroDiffLineResponse(
                    "added",
                    null,
                    newIndex + 1,
                    newLines[newIndex]));
                added++;
                newIndex++;
            }
        }

        while (oldIndex < rows)
        {
            output.Add(new MacroDiffLineResponse(
                "removed",
                oldIndex + 1,
                null,
                oldLines[oldIndex]));
            removed++;
            oldIndex++;
        }

        while (newIndex < columns)
        {
            output.Add(new MacroDiffLineResponse(
                "added",
                null,
                newIndex + 1,
                newLines[newIndex]));
            added++;
            newIndex++;
        }
    }

    private static void AddNaiveDiff(
        List<MacroDiffLineResponse> output,
        string[] oldLines,
        string[] newLines,
        ref int added,
        ref int removed,
        ref int unchanged)
    {
        var index = 0;
        while (index < oldLines.Length
               && index < newLines.Length
               && string.Equals(oldLines[index], newLines[index], StringComparison.Ordinal))
        {
            output.Add(new MacroDiffLineResponse("same", index + 1, index + 1, newLines[index]));
            unchanged++;
            index++;
        }

        for (var oldIndex = index; oldIndex < oldLines.Length; oldIndex++)
        {
            output.Add(new MacroDiffLineResponse("removed", oldIndex + 1, null, oldLines[oldIndex]));
            removed++;
        }

        for (var newIndex = index; newIndex < newLines.Length; newIndex++)
        {
            output.Add(new MacroDiffLineResponse("added", null, newIndex + 1, newLines[newIndex]));
            added++;
        }
    }
}
