using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stp.Core.Storage;
using Stp.Core.Summary;
using Stp.Infrastructure.Excel;
using Stp.Infrastructure.Storage;

namespace Stp.Tests.Excel;

/// <summary>
/// "Danh mục học được": mục người dùng tự lưu phải đứng ngay sau tiêu đề sheet
/// (không bao giờ bị văng khỏi danh mục), meta có UsageCount+. nguồn đúng,
/// biến thể dấu/khoảng trắng gộp về một khoá.
/// </summary>
public sealed class CatalogDictionaryTests
{
    private sealed class FakeCatalogDictionary : ICatalogDictionary
    {
        private readonly List<CatalogEntry> _entries = [];

        public FakeCatalogDictionary Add(string display, int usage = 1)
        {
            _entries.Add(new CatalogEntry(
                Guid.NewGuid(),
                display,
                CatalogKey.Normalize(display),
                CatalogEntrySource.User,
                usage,
                DateTime.UtcNow,
                null));
            return this;
        }

        public IReadOnlyList<CatalogEntry> GetActive() => _entries;

        public CatalogEntry? Find(string normalizedKey) =>
            _entries.FirstOrDefault(entry =>
                entry.NormalizedKey == CatalogKey.Normalize(normalizedKey));

        public CatalogEntry? GetById(Guid id) =>
            _entries.FirstOrDefault(entry => entry.Id == id);

        public CatalogEntry Upsert(
            string displayName,
            string? dossierTypeHint,
            CatalogEntrySource source,
            int? usageCount = null)
        {
            var existing = Find(displayName);
            if (existing is not null)
            {
                return existing;
            }

            var entry = new CatalogEntry(
                Guid.NewGuid(),
                displayName,
                CatalogKey.Normalize(displayName),
                source,
                usageCount ?? 1,
                DateTime.UtcNow,
                dossierTypeHint);
            _entries.Add(entry);
            return entry;
        }

        public void Rename(Guid id, string displayName)
        {
        }

        public void SetDisabled(Guid id, bool disabled)
        {
        }
    }

    [Fact]
    public async Task ReadSession_MergesUserDictionaryAboveObserved()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var dictionary = new FakeCatalogDictionary()
                .Add("tờ khai cư trú", usage: 900)
                .Add("mẫu đơn xin việc", usage: 12);
            var storage = new FileStorage(Options.Create(new StorageOptions
            {
                RootPath = Path.Combine(directory, "data")
            }));
            var service = new SummaryToolService(
                storage,
                NullLogger<SummaryToolService>.Instance,
                null,
                dictionary);
            var session = await OpenSessionAsync(service, source);

            // Mục người dùng đã lưu — kể cả chưa có trong file này — có mặt.
            Assert.Contains("tờ khai cư trú", session.Catalog);
            Assert.Contains("mẫu đơn xin việc", session.Catalog);

            // Meta: badge ×N từ UsageCount thật, nguồn đúng "user".
            var userMeta = session.CatalogMeta
                .FirstOrDefault(entry => entry.Name == "tờ khai cư trú");
            Assert.NotNull(userMeta);
            Assert.Equal("user", userMeta.Source);
            Assert.Equal(900, userMeta.UsageCount);

            // Thứ tự: mục user nằm trên phần observed của file.
            var userIndex = IndexOf(session.Catalog, "tờ khai cư trú");
            var observedIndex = IndexOf(session.Catalog, "giấy nhập cảnh");
            Assert.True(userIndex >= 0 && userIndex < observedIndex);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (string.Equals(list[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    [Fact]
    public async Task ReadSession_SessionWithoutDictionaryStillBuildsMeta()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = CreateWorkbook(directory);
            var storage = new FileStorage(Options.Create(new StorageOptions
            {
                RootPath = Path.Combine(directory, "data")
            }));
            var service = new SummaryToolService(storage, NullLogger<SummaryToolService>.Instance);
            var session = await OpenSessionAsync(service, source);

            var fileMeta = session.CatalogMeta
                .FirstOrDefault(entry => entry.Name == "giấy nhập cảnh");
            Assert.NotNull(fileMeta);
            Assert.Equal("file", fileMeta.Source);
            Assert.Equal(1, fileMeta.UsageCount);

            var defaultMeta = session.CatalogMeta
                .FirstOrDefault(entry => entry.Name == "trích lục ghi chú kết hôn");
            Assert.NotNull(defaultMeta);
            Assert.Equal("default", defaultMeta.Source);
            Assert.Equal(0, defaultMeta.UsageCount);

            // Các họ tiền tố người dùng yêu cầu (10/11): cam kết, chứng minh,
            // biến thể thật "đơn xin cấp giấy chứng nhận độc thân" — đều có mặt
            // ngay cả khi file không chứa (đệm compose DefaultCatalog).
            Assert.Contains("cam kết hôn nhân thực tế", session.Catalog);
            Assert.Contains("đơn xin xác nhận độc thân", session.Catalog);
            Assert.Contains("đơn xin cấp giấy chứng nhận độc thân", session.Catalog);
            Assert.Contains("chứng minh nhân dân", session.Catalog);
            Assert.Contains("quyết định nuôi con nuôi", session.Catalog);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("Đơn Xin Đăng Ký", "don xin dang ky")]
    [InlineData("ĐƠN XIN HỘ KHẨU", "đơn xin hộ khẩu")]
    [InlineData("PHIẾU   đề    xuất", "phiếu đề xuất")]
    public void CatalogKey_FoldsDiacriticsAndSpace(string first, string second)
    {
        Assert.Equal(CatalogKey.Normalize(first), CatalogKey.Normalize(second));
    }

    private static async Task<SummaryWorkbook> OpenSessionAsync(
        SummaryToolService service,
        string source)
    {
        await using var stream = File.OpenRead(source);
        return await service.CreateSessionAsync(stream, Path.GetFileName(source));
    }

    private static string CreateTempDirectory() =>
        Path.Combine(Path.GetTempPath(), "stp-catalog", Guid.NewGuid().ToString("N"));

    private static string CreateWorkbook(string directory)
    {
        var path = Path.Combine(directory, "sample.xlsx");
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("ML");
        sheet.Cell("A1").Value = "Hộp số";
        sheet.Cell("C1").Value = "Hồ sơ số";
        sheet.Cell("D1").Value = "Trích yếu nội dung";
        sheet.Cell("E1").Value = "Thời gian tài liệu";
        sheet.Cell("F1").Value = "Số tờ";
        sheet.Cell("G1").Value = "Ghi chú";

        sheet.Cell("D2").Value = "I. Văn phòng";
        sheet.Cell("D3").Value = "NĂM 1990";
        sheet.Cell("D4").Value = "* Dang ky ket hon";

        // Danh mục in sẵn dọc/trên trang.
        sheet.Cell("S4").Value = "phiếu đề xuất";
        sheet.Cell("T4").Value = "giấy biên nhận";
        sheet.Cell("U4").Value = "giấy khai sinh";
        sheet.Cell("V4").Value = "hộ chiếu";

        sheet.Cell("B5").Value = 36;
        sheet.Cell("E5").Value = new DateTime(1990, 1, 23);
        sheet.Cell("D5").Value =
            "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà NGUYỄN THỊ A và Ông JOHN SMITH "
            + "(phiếu đề xuất, giấy nhập cảnh)";

        sheet.Cell("B6").Value = 37;
        sheet.Cell("E6").Value = new DateTime(1990, 1, 24);
        sheet.Cell("D6").Value =
            "Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn "
            + "với người nước ngoài của Bà TRẦN THỊ B và Ông NGUYỄN VĂN C";

        workbook.SaveAs(path);
        return path;
    }
}
