namespace Stp.Core.Macros;

public class MacroVersionSummary
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "StpArchiveMacro";

    public int VersionNumber { get; init; }

    public string CodeHash { get; init; } = string.Empty;

    public string? ChangeNote { get; init; }

    public string CreatedBy { get; init; } = "system";

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsActive { get; init; }

    public Guid? ParentVersionId { get; init; }

    public int LineCount { get; init; }
}

public sealed class MacroVersionDetail : MacroVersionSummary
{
    public string Code { get; init; } = string.Empty;
}

public sealed class MacroSaveRequest
{
    public string Code { get; init; } = string.Empty;

    public string? ChangeNote { get; init; }

    public string? CreatedBy { get; init; }

    public Guid? ParentVersionId { get; init; }

    public bool Activate { get; init; } = true;
}

public sealed class MacroValidationResult
{
    public bool IsValid { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Quản lý phiên bản macro VBA. Phiên bản active được dùng khi chuyển đổi workbook;
/// nếu DB chưa có phiên bản nào, mã mặc định trong assembly được seed thành v1.
/// </summary>
public interface IMacroService
{
    Task<IReadOnlyList<MacroVersionSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<MacroVersionDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MacroVersionDetail> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<string> GetActiveCodeAsync(CancellationToken cancellationToken = default);

    Task<MacroVersionDetail> SaveAsync(
        MacroSaveRequest request,
        CancellationToken cancellationToken = default);

    Task<MacroVersionDetail?> ActivateAsync(Guid id, CancellationToken cancellationToken = default);

    MacroValidationResult Validate(string code);
}
