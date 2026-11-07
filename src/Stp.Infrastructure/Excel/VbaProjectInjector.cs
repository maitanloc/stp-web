using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stp.Core.Excel;

namespace Stp.Infrastructure.Excel;

public sealed class VbaInjectionOptions
{
    /// <summary>
    /// Đường dẫn tới workbook .xlsm mẫu có VBA project sạch. Để trống thì dùng
    /// donor đóng gói trong assembly (1 sheet module + ThisWorkbook).
    /// </summary>
    public string? DonorWorkbookPath { get; set; }

    /// <summary>Bật/tắt chèn macro khi engine không có VBA runtime (WPS).</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class VbaInjectionResult
{
    public bool Injected { get; init; }

    public string OutputPath { get; init; } = string.Empty;

    public string WorkbookCodeName { get; init; } = string.Empty;

    public int DonorSheetModules { get; init; }

    public int MappedSheetModules { get; init; }

    public int SheetsWithoutModules { get; init; }

    public int MacroLines { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Chèn VBA project vào file .xlsm ở mức OpenXML/ZIP, không cần Excel hay VBA runtime.
/// Dùng cho máy chỉ có WPS (WPS không đọc/ghi được VBProject).
///
/// Nguyên tắc: donor chỉ nên có số sheet module &lt;= số sheet của file đích. Các sheet
/// không có module tương ứng sẽ được bỏ codeName để Excel tự tạo module khi mở file.
/// Nhờ vậy không sinh ra module mồ côi (nguyên nhân làm macro không chạy).
/// </summary>
public sealed class VbaProjectInjector
{
    private const string ProjectResource = "Stp.Infrastructure.Excel.Templates.vbaProject.bin";
    private const string MapResource = "Stp.Infrastructure.Excel.Templates.vba-donor.json";
    private const string MainSheetContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string MacroMainContentType =
        "application/vnd.ms-excel.sheet.macroEnabled.main+xml";
    private const string VbaContentType = "application/vnd.ms-office.vbaProject";
    private const string VbaRelationshipType =
        "http://schemas.microsoft.com/office/2006/relationships/vbaProject";

    private static readonly XNamespace MainNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypesNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    private readonly ILogger<VbaProjectInjector> _logger;
    private readonly VbaInjectionOptions _options;
    private readonly Lazy<DonorProject> _embeddedDonor;

    public VbaProjectInjector(
        IOptions<VbaInjectionOptions> options,
        ILogger<VbaProjectInjector> logger)
    {
        _options = options.Value;
        _logger = logger;
        _embeddedDonor = new Lazy<DonorProject>(
            LoadEmbeddedDonor,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable =>
        _options.Enabled
        && (_embeddedDonor.Value.Project.Length > 0
            || (_options.DonorWorkbookPath is { Length: > 0 } path && File.Exists(path)));

    public VbaInjectionResult Inject(string targetPath, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("Chức năng chèn VBA đang tắt trong cấu hình.");
        }

        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!File.Exists(fullTargetPath))
        {
            throw new FileNotFoundException("Không tìm thấy file đích để chèn macro.", fullTargetPath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var donor = LoadDonor();
        var warnings = new List<string>();
        var tempPath = fullTargetPath + ".vba-" + Guid.NewGuid().ToString("N") + ".tmp";
        Dictionary<int, string?> mapping;
        var sheetCount = 0;

        try
        {
            File.Copy(fullTargetPath, tempPath, overwrite: true);

            using (var archive = ZipFile.Open(tempPath, ZipArchiveMode.Update))
            {
                var workbookEntry = archive.GetEntry("xl/workbook.xml")
                    ?? throw new InvalidDataException(
                        "File không phải workbook OpenXML hợp lệ: thiếu xl/workbook.xml.");

                var workbook = LoadXml(workbookEntry);
                var relationships = LoadRelationships(archive);
                var sheetInfos = ReadTargetSheets(archive, workbook, relationships, warnings);
                sheetCount = sheetInfos.Count;
                mapping = MapDonorSheets(donor, sheetInfos, warnings);

                foreach (var sheet in sheetInfos)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ApplySheetCodeName(archive, sheet, mapping, warnings);
                }

                ApplyWorkbookCodeName(workbook, donor.WorkbookCodeName);
                SaveXml(workbookEntry, workbook);

                UpdateContentTypes(archive);
                UpdateWorkbookRelationships(archive);
                WriteProject(archive, donor.Project);
            }

            File.Move(tempPath, fullTargetPath, overwrite: true);

            var mapped = mapping.Values.Count(v => v is not null);
            _logger.LogInformation(
                "Đã chèn VBA donor vào {Path}: {Mapped}/{DonorSheets} sheet module, {Without} sheet không module",
                fullTargetPath,
                mapped,
                donor.Sheets.Count,
                Math.Max(0, sheetCount - mapped));

            return new VbaInjectionResult
            {
                Injected = true,
                OutputPath = fullTargetPath,
                WorkbookCodeName = donor.WorkbookCodeName,
                DonorSheetModules = donor.Sheets.Count,
                MappedSheetModules = mapped,
                SheetsWithoutModules = Math.Max(0, sheetCount - mapped),
                MacroLines = donor.MacroLines,
                Warnings = warnings
            };
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private DonorProject LoadDonor()
    {
        var configured = _options.DonorWorkbookPath;
        if (configured is { Length: > 0 } && File.Exists(configured))
        {
            return LoadDonorFromWorkbook(Path.GetFullPath(configured));
        }

        if (configured is { Length: > 0 })
        {
            _logger.LogWarning(
                "Không tìm thấy donor workbook '{Path}'; dùng donor đóng gói trong assembly",
                configured);
        }

        return _embeddedDonor.Value;
    }

    private static DonorProject LoadEmbeddedDonor()
    {
        var assembly = typeof(VbaProjectInjector).Assembly;
        using var projectStream = assembly.GetManifestResourceStream(ProjectResource)
            ?? throw new InvalidOperationException(
                $"Không tìm thấy VBA donor '{ProjectResource}' trong assembly.");
        using var memory = new MemoryStream();
        projectStream.CopyTo(memory);

        using var mapStream = assembly.GetManifestResourceStream(MapResource)
            ?? throw new InvalidOperationException(
                $"Không tìm thấy bản đồ VBA donor '{MapResource}' trong assembly.");

        var map = JsonSerializer.Deserialize<DonorMap>(mapStream, JsonOptions)
            ?? throw new InvalidOperationException("Bản đồ VBA donor không hợp lệ.");

        ValidateProject(memory.ToArray());
        return new DonorProject(
            memory.ToArray(),
            map.WorkbookCodeName,
            map.Sheets.Select(s => new DonorSheet(s.Name, s.CodeName)).ToArray(),
            "embedded",
            map.MacroLines);
    }

    private static DonorProject LoadDonorFromWorkbook(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var projectEntry = archive.GetEntry("xl/vbaProject.bin")
            ?? throw new InvalidDataException(
                $"Workbook donor '{path}' không có xl/vbaProject.bin.");
        using var projectStream = projectEntry.Open();
        using var memory = new MemoryStream();
        projectStream.CopyTo(memory);

        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidDataException(
                $"Workbook donor '{path}' không có xl/workbook.xml.");
        var workbook = LoadXml(workbookEntry);
        var relationships = LoadRelationships(archive);
        var sheets = ReadTargetSheets(archive, workbook, relationships, new List<string>());

        var workbookCodeName = workbook.Root?
            .Element(MainNs + "workbookPr")?
            .Attribute("codeName")?
            .Value;
        if (string.IsNullOrWhiteSpace(workbookCodeName))
        {
            workbookCodeName = "ThisWorkbook";
        }

        var donorSheets = sheets
            .Select(s => new DonorSheet(s.Name, s.CodeName ?? string.Empty))
            .Where(s => !string.IsNullOrWhiteSpace(s.CodeName))
            .ToArray();

        if (donorSheets.Length == 0)
        {
            throw new InvalidDataException(
                $"Workbook donor '{path}' không có sheet module/codeName nào.");
        }

        var bytes = memory.ToArray();
        ValidateProject(bytes);
        return new DonorProject(bytes, workbookCodeName, donorSheets, path, 0);
    }

    private static void ValidateProject(byte[] bytes)
    {
        var oleSignature = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        if (bytes.Length < 512
            || !bytes.AsSpan(0, oleSignature.Length).SequenceEqual(oleSignature))
        {
            throw new InvalidDataException(
                "vbaProject.bin không phải OLE compound file hợp lệ (có thể file bị hỏng).");
        }
    }

    private static List<TargetSheet> ReadTargetSheets(
        ZipArchive archive,
        XDocument workbook,
        IReadOnlyDictionary<string, string> relationships,
        List<string> warnings)
    {
        var result = new List<TargetSheet>();
        var sheets = workbook.Root?
            .Element(MainNs + "sheets")?
            .Elements(MainNs + "sheet")
            .ToList() ?? new List<XElement>();

        var index = 0;
        foreach (var sheet in sheets)
        {
            index++;
            var name = (string?)sheet.Attribute("name") ?? $"Sheet{index}";
            var relationshipId = (string?)sheet.Attribute(RelNs + "id");
            var target = relationshipId is not null
                ? relationships.GetValueOrDefault(relationshipId)
                : null;

            if (target is null)
            {
                warnings.Add($"Sheet '{name}' không có relationship; bỏ qua khi gán codeName.");
                result.Add(new TargetSheet(name, index, null, null, null));
                continue;
            }

            var partPath = ResolvePartPath("xl/", target);
            var entry = archive.GetEntry(partPath);
            if (entry is null)
            {
                warnings.Add($"Không tìm thấy part '{partPath}' của sheet '{name}'.");
                result.Add(new TargetSheet(name, index, null, null, null));
                continue;
            }

            var worksheet = LoadXml(entry);
            var codeName = worksheet.Root?
                .Element(MainNs + "sheetPr")?
                .Attribute("codeName")?
                .Value;

            result.Add(new TargetSheet(name, index, entry, worksheet, codeName));
        }

        return result;
    }

    private static Dictionary<int, string?> MapDonorSheets(
        DonorProject donor,
        IReadOnlyList<TargetSheet> targetSheets,
        List<string> warnings)
    {
        var mapping = new Dictionary<int, string?>();
        var usedTargetIndexes = new HashSet<int>();

        // 1) Ưu tiên khớp theo tên sheet.
        foreach (var donorSheet in donor.Sheets)
        {
            var target = targetSheets.FirstOrDefault(t =>
                t.Worksheet is not null
                && !usedTargetIndexes.Contains(t.Index)
                && string.Equals(t.Name, donorSheet.Name, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                continue;
            }

            mapping[target.Index] = donorSheet.CodeName;
            usedTargetIndexes.Add(target.Index);
        }

        // 2) Donor sheet chưa khớp thì gán vào các sheet còn trống theo thứ tự.
        var remainingDonorSheets = donor.Sheets
            .Where(d => !mapping.Values.Contains(d.CodeName, StringComparer.OrdinalIgnoreCase))
            .ToList();
        foreach (var donorSheet in remainingDonorSheets)
        {
            var target = targetSheets.FirstOrDefault(t =>
                !usedTargetIndexes.Contains(t.Index) && t.Worksheet is not null);
            if (target is null)
            {
                warnings.Add(
                    $"Donor có sheet module '{donorSheet.CodeName}' nhưng file đích không còn sheet trống để gắn. " +
                    "Module này sẽ mồ côi; nên tạo donor ít sheet hơn hoặc bằng file đích.");
                break;
            }

            mapping[target.Index] = donorSheet.CodeName;
            usedTargetIndexes.Add(target.Index);
        }

        foreach (var sheet in targetSheets)
        {
            mapping.TryAdd(sheet.Index, null);
        }

        return mapping;
    }

    private static void ApplySheetCodeName(
        ZipArchive archive,
        TargetSheet sheet,
        IReadOnlyDictionary<int, string?> mapping,
        List<string> warnings)
    {
        if (sheet.Entry is null || sheet.Worksheet is null)
        {
            return;
        }

        var codeName = mapping.GetValueOrDefault(sheet.Index);
        var worksheet = sheet.Worksheet;
        var sheetProperties = worksheet.Root?.Element(MainNs + "sheetPr");

        if (string.IsNullOrWhiteSpace(codeName))
        {
            // Không có module tương ứng: bỏ codeName cũ để Excel tự tạo module mới.
            if (sheetProperties?.Attribute("codeName") is not null)
            {
                sheetProperties.Attribute("codeName")!.Remove();
            }

            SaveXml(sheet.Entry, worksheet);
            return;
        }

        if (sheetProperties is null)
        {
            sheetProperties = new XElement(MainNs + "sheetPr");
            worksheet.Root?.AddFirst(sheetProperties);
        }

        sheetProperties.SetAttributeValue("codeName", codeName);
        SaveXml(sheet.Entry, worksheet);
    }

    private static void ApplyWorkbookCodeName(XDocument workbook, string codeName)
    {
        var root = workbook.Root
            ?? throw new InvalidDataException("xl/workbook.xml không có root element.");
        var workbookProperties = root.Element(MainNs + "workbookPr");

        if (workbookProperties is null)
        {
            workbookProperties = new XElement(MainNs + "workbookPr");
            var fileVersion = root.Element(MainNs + "fileVersion");
            if (fileVersion is not null)
            {
                fileVersion.AddAfterSelf(workbookProperties);
            }
            else
            {
                root.AddFirst(workbookProperties);
            }
        }

        workbookProperties.SetAttributeValue("codeName", codeName);
    }

    private static void UpdateContentTypes(ZipArchive archive)
    {
        var entry = archive.GetEntry("[Content_Types].xml")
            ?? throw new InvalidDataException("File thiếu [Content_Types].xml.");
        var document = LoadXml(entry);
        var root = document.Root
            ?? throw new InvalidDataException("[Content_Types].xml không có root element.");

        var workbookOverride = root
            .Elements(ContentTypesNs + "Override")
            .FirstOrDefault(e => string.Equals(
                (string?)e.Attribute("PartName"),
                "/xl/workbook.xml",
                StringComparison.OrdinalIgnoreCase));

        workbookOverride ??= root
            .Elements(ContentTypesNs + "Override")
            .FirstOrDefault(e => string.Equals(
                (string?)e.Attribute("ContentType"),
                MainSheetContentType,
                StringComparison.OrdinalIgnoreCase));

        if (workbookOverride is null)
        {
            workbookOverride = new XElement(
                ContentTypesNs + "Override",
                new XAttribute("PartName", "/xl/workbook.xml"),
                new XAttribute("ContentType", MacroMainContentType));
            root.Add(workbookOverride);
        }
        else
        {
            workbookOverride.SetAttributeValue("ContentType", MacroMainContentType);
        }

        var hasVbaOverride = root
            .Elements(ContentTypesNs + "Override")
            .Any(e => string.Equals(
                (string?)e.Attribute("PartName"),
                "/xl/vbaProject.bin",
                StringComparison.OrdinalIgnoreCase));

        if (!hasVbaOverride)
        {
            root.Add(new XElement(
                ContentTypesNs + "Override",
                new XAttribute("PartName", "/xl/vbaProject.bin"),
                new XAttribute("ContentType", VbaContentType)));
        }

        SaveXml(entry, document);
    }

    private static void UpdateWorkbookRelationships(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("File thiếu xl/_rels/workbook.xml.rels.");
        var document = LoadXml(entry);
        var root = document.Root
            ?? throw new InvalidDataException("workbook.xml.rels không có root element.");

        foreach (var relationship in root.Elements(PackageRelNs + "Relationship").ToList())
        {
            var type = (string?)relationship.Attribute("Type") ?? string.Empty;
            if (type.EndsWith("/vbaProject", StringComparison.OrdinalIgnoreCase))
            {
                relationship.Remove();
            }
        }

        var usedIds = root
            .Elements(PackageRelNs + "Relationship")
            .Select(e => (string?)e.Attribute("Id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var relationshipId = "rIdVba";
        var suffix = 1;
        while (usedIds.Contains(relationshipId))
        {
            relationshipId = "rIdVba" + suffix++;
        }

        root.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", relationshipId),
            new XAttribute("Type", VbaRelationshipType),
            new XAttribute("Target", "vbaProject.bin")));

        SaveXml(entry, document);
    }

    private static void WriteProject(ZipArchive archive, byte[] project)
    {
        var existing = archive.GetEntry("xl/vbaProject.bin");
        existing?.Delete();

        var entry = archive.CreateEntry("xl/vbaProject.bin", CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(project);
    }

    private static IReadOnlyDictionary<string, string> LoadRelationships(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidDataException("File thiếu xl/_rels/workbook.xml.rels.");
        var document = LoadXml(entry);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in document.Root?.Elements(PackageRelNs + "Relationship")
                     ?? Enumerable.Empty<XElement>())
        {
            var id = (string?)relationship.Attribute("Id");
            var target = (string?)relationship.Attribute("Target");
            if (!string.IsNullOrWhiteSpace(id)
                && !string.IsNullOrWhiteSpace(target)
                && !string.Equals((string?)relationship.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
            {
                result[id] = target;
            }
        }

        return result;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }

    private static void SaveXml(ZipArchiveEntry entry, XDocument document)
    {
        using var stream = entry.Open();
        stream.SetLength(0);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.Save(writer, SaveOptions.DisableFormatting);
        writer.Flush();
    }

    private static string ResolvePartPath(string basePath, string target)
    {
        var normalized = target.Replace('\\', '/');
        if (normalized.StartsWith('/'))
        {
            return normalized.TrimStart('/');
        }

        var combined = basePath + normalized;
        var segments = new List<string>();
        foreach (var segment in combined.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Bỏ qua file tạm không xóa được.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record DonorProject(
        byte[] Project,
        string WorkbookCodeName,
        IReadOnlyList<DonorSheet> Sheets,
        string Source,
        int MacroLines);

    private sealed record DonorSheet(string Name, string CodeName);

    private sealed record TargetSheet(
        string Name,
        int Index,
        ZipArchiveEntry? Entry,
        XDocument? Worksheet,
        string? CodeName);

    private sealed class DonorMap
    {
        public string WorkbookCodeName { get; set; } = "ThisWorkbook";

        public int MacroLines { get; set; }

        public List<DonorMapSheet> Sheets { get; set; } = new();
    }

    private sealed class DonorMapSheet
    {
        public string Name { get; set; } = string.Empty;

        public string CodeName { get; set; } = string.Empty;
    }
}
