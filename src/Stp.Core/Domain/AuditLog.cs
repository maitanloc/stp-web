namespace Stp.Core.Domain;

/// <summary>
/// Nhật ký thao tác developer/admin.
/// </summary>
public sealed class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string Actor { get; set; } = "system";

    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public string? Details { get; set; }
}
