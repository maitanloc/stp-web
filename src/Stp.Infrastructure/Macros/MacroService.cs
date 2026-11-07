using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stp.Core.Domain;
using Stp.Core.Macros;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Persistence;

namespace Stp.Infrastructure.Macros;

/// <summary>
/// Lưu và kích hoạt phiên bản macro trong SQL Server. Kiểm tra tĩnh các marker
/// bắt buộc của StpArchiveMacro.bas trước khi lưu.
/// </summary>
public sealed class MacroService : IMacroService
{
    private const string DefaultName = "StpArchiveMacro";
    private const int MaxCodeLength = 1024 * 1024;

    private readonly StpDbContext _db;
    private readonly MacroTemplateProvider _templateProvider;
    private readonly ILogger<MacroService> _logger;

    public MacroService(
        StpDbContext db,
        MacroTemplateProvider templateProvider,
        ILogger<MacroService> logger)
    {
        _db = db;
        _templateProvider = templateProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MacroVersionSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);

        var versions = await _db.MacroVersions
            .AsNoTracking()
            .OrderByDescending(version => version.VersionNumber)
            .ThenByDescending(version => version.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return versions.Select(ToSummary).ToList();
    }

    public async Task<MacroVersionDetail?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);

        var version = await _db.MacroVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return version is null ? null : ToDetail(version);
    }

    public async Task<MacroVersionDetail> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);

        var version = await _db.MacroVersions
            .AsNoTracking()
            .Where(entity => entity.IsActive)
            .OrderByDescending(entity => entity.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không có phiên bản macro nào đang active.");

        return ToDetail(version);
    }

    public async Task<string> GetActiveCodeAsync(CancellationToken cancellationToken = default)
    {
        var active = await GetActiveAsync(cancellationToken).ConfigureAwait(false);
        return active.Code;
    }

    public async Task<MacroVersionDetail> SaveAsync(
        MacroSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request.Code);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                "Macro không hợp lệ: " + string.Join(" ", validation.Errors));
        }

        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);

        if (request.Activate)
        {
            await DeactivateAllAsync(cancellationToken).ConfigureAwait(false);
        }

        var maxVersion = await _db.MacroVersions
            .MaxAsync(version => (int?)version.VersionNumber, cancellationToken)
            .ConfigureAwait(false) ?? 0;

        var entity = new MacroVersion
        {
            Name = DefaultName,
            VersionNumber = maxVersion + 1,
            Code = request.Code.ReplaceLineEndings("\r\n"),
            CodeHash = ComputeHash(request.Code.ReplaceLineEndings("\r\n")),
            ChangeNote = string.IsNullOrWhiteSpace(request.ChangeNote)
                ? null
                : request.ChangeNote.Trim(),
            CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy)
                ? "developer"
                : request.CreatedBy.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = request.Activate,
            ParentVersionId = request.ParentVersionId
        };

        _db.MacroVersions.Add(entity);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Đã lưu macro v{Version} ({Lines} dòng, active={Active})",
            entity.VersionNumber,
            CountLines(entity.Code),
            entity.IsActive);

        return ToDetail(entity);
    }

    public async Task<MacroVersionDetail?> ActivateAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);

        var version = await _db.MacroVersions
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (version is null)
        {
            return null;
        }

        await DeactivateAllAsync(cancellationToken).ConfigureAwait(false);
        version.IsActive = true;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Đã kích hoạt macro v{Version}", version.VersionNumber);
        return ToDetail(version);
    }

    public MacroValidationResult Validate(string code)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(code))
        {
            return new MacroValidationResult
            {
                IsValid = false,
                Errors = new[] { "Mã macro đang trống." }
            };
        }

        if (code.Length > MaxCodeLength)
        {
            errors.Add($"Mã macro vượt quá {MaxCodeLength / 1024} KB.");
        }

        Require("Option Explicit", "Thiếu 'Option Explicit'.");
        Require("Workbook_Open", "Thiếu sự kiện Workbook_Open.");
        Require("Workbook_SheetActivate", "Thiếu sự kiện Workbook_SheetActivate.");
        Require("StpSelfTest", "Thiếu hàm kiểm tra StpSelfTest.");
        Require("BANG_IN_HANG_LOAT", "Thiếu marker BANG_IN_HANG_LOAT.");
        Require("FIRST_ROW", "Thiếu hằng FIRST_ROW.");
        Require("BLOCK", "Thiếu hằng BLOCK.");

        var subStarts = CountToken(code, "Sub ");
        var subEnds = CountToken(code, "End Sub");
        if (subStarts != subEnds)
        {
            errors.Add(
                $"Số 'Sub' ({subStarts}) không khớp 'End Sub' ({subEnds}).");
        }

        if (code.Contains("Declare Function", StringComparison.OrdinalIgnoreCase)
            || code.Contains("Declare PtrSafe", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Macro có khai báo API bên ngoài (Declare); cần kiểm tra kỹ trên Excel 64-bit.");
        }

        if (code.Contains("Shell(", StringComparison.OrdinalIgnoreCase)
            || code.Contains("CreateObject(", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Macro dùng Shell/CreateObject; chỉ chấp nhận khi thật sự cần.");
        }

        if (code.Contains("On Error Resume Next", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Macro dùng 'On Error Resume Next'; lỗi có thể bị bỏ qua âm thầm.");
        }

        if (!code.Contains("EnableEvents", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Không thấy 'EnableEvents'; kiểm tra sự kiện sau khi đóng file.");
        }

        return new MacroValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };

        void Require(string token, string message)
        {
            if (!code.Contains(token, StringComparison.Ordinal))
            {
                errors.Add(message);
            }
        }
    }

    private async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        if (await _db.MacroVersions.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var code = _templateProvider.DefaultMacroCode.ReplaceLineEndings("\r\n");
        _db.MacroVersions.Add(new MacroVersion
        {
            Name = DefaultName,
            VersionNumber = 1,
            Code = code,
            CodeHash = ComputeHash(code),
            ChangeNote = "Phiên bản mặc định từ StpArchiveMacro.bas",
            CreatedBy = "system",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DeactivateAllAsync(CancellationToken cancellationToken)
    {
        var activeVersions = await _db.MacroVersions
            .Where(version => version.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var version in activeVersions)
        {
            version.IsActive = false;
        }
    }

    private static MacroVersionSummary ToSummary(MacroVersion version) =>
        new()
        {
            Id = version.Id,
            Name = version.Name,
            VersionNumber = version.VersionNumber,
            CodeHash = version.CodeHash,
            ChangeNote = version.ChangeNote,
            CreatedBy = version.CreatedBy,
            CreatedAt = version.CreatedAt,
            IsActive = version.IsActive,
            ParentVersionId = version.ParentVersionId,
            LineCount = CountLines(version.Code)
        };

    private static MacroVersionDetail ToDetail(MacroVersion version) =>
        new()
        {
            Id = version.Id,
            Name = version.Name,
            VersionNumber = version.VersionNumber,
            CodeHash = version.CodeHash,
            ChangeNote = version.ChangeNote,
            CreatedBy = version.CreatedBy,
            CreatedAt = version.CreatedAt,
            IsActive = version.IsActive,
            ParentVersionId = version.ParentVersionId,
            LineCount = CountLines(version.Code),
            Code = version.Code
        };

    private static int CountLines(string code) =>
        string.IsNullOrEmpty(code)
            ? 0
            : code.ReplaceLineEndings("\n").Split('\n').Length;

    private static int CountToken(string code, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = code.IndexOf(token, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static string ComputeHash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
