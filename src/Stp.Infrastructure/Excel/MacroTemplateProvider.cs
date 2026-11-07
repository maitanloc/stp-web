using System.Reflection;

namespace Stp.Infrastructure.Excel;

/// <summary>
/// Cung cấp mã macro mặc định đóng gói trong assembly. Khi developer tạo phiên bản
/// mới trong DB, service sẽ ưu tiên phiên bản active (bổ sung ở phase Developer UI).
/// </summary>
public sealed class MacroTemplateProvider
{
    private const string ResourceName = "Stp.Infrastructure.Excel.Templates.StpArchiveMacro.bas";
    private readonly Lazy<string> _defaultCode;

    public MacroTemplateProvider()
    {
        _defaultCode = new Lazy<string>(LoadDefault, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string DefaultMacroCode => _defaultCode.Value;

    private static string LoadDefault()
    {
        var assembly = typeof(MacroTemplateProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException(
                               $"Không tìm thấy macro mặc định '{ResourceName}' trong assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<string> ListResources()
    {
        return typeof(MacroTemplateProvider).Assembly
            .GetManifestResourceNames()
            .Where(n => n.Contains("Templates", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
