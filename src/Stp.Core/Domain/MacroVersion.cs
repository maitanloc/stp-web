namespace Stp.Core.Domain;

/// <summary>
/// Một phiên bản mã macro. Code là nguồn chuẩn để build file .xlsm.
/// </summary>
public sealed class MacroVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "StpArchiveMacro";

    public int VersionNumber { get; set; }

    public string Code { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;

    public string? ChangeNote { get; set; }

    public string CreatedBy { get; set; } = "system";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive { get; set; }

    public Guid? ParentVersionId { get; set; }

    public MacroVersion? ParentVersion { get; set; }
}
