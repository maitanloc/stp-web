using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stp.Core.Domain;
using Stp.Core.Excel;
using Stp.Core.Sync;

namespace Stp.Infrastructure.Persistence;

/// <summary>
/// Đồng bộ workbook Excel lên database. ML là nguồn dữ liệu chính:
/// hồ sơ mới được thêm, hồ sơ đổi được cập nhật, hồ sơ không còn trong ML bị xoá.
/// Xung đột version được ghi vào SyncChange và áp dụng last-write-wins.
/// </summary>
public sealed class ArchiveSyncService : IArchiveSyncService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly StpDbContext _db;
    private readonly IExcelInspector _inspector;
    private readonly ILogger<ArchiveSyncService> _logger;

    public ArchiveSyncService(
        StpDbContext db,
        IExcelInspector inspector,
        ILogger<ArchiveSyncService> logger)
    {
        _db = db;
        _inspector = inspector;
        _logger = logger;
    }

    public async Task<SyncOutcome> ApplyAsync(
        SyncRequest request,
        CancellationToken cancellationToken = default)
    {
        var sourceFileName = string.IsNullOrWhiteSpace(request.SourceFileName)
            ? Path.GetFileName(request.FilePath)
            : request.SourceFileName;

        try
        {
            if (!File.Exists(request.FilePath))
            {
                throw new FileNotFoundException("Không tìm thấy file đồng bộ.", request.FilePath);
            }

            var snapshot = _inspector.Read(request.FilePath);
            if (snapshot.Kind == WorkbookKind.Other)
            {
                throw new InvalidDataException(
                    "File không có sheet dữ liệu (ML/file nhập) nên không thể đồng bộ.");
            }

            var fileHash = request.FileHash ?? await ComputeHashAsync(
                request.FilePath,
                cancellationToken).ConfigureAwait(false);

            var workbook = await FindWorkbookAsync(
                request.WorkbookId,
                sourceFileName,
                fileHash,
                cancellationToken).ConfigureAwait(false);

            if (workbook is null)
            {
                workbook = new ArchiveWorkbook
                {
                    Name = Path.GetFileNameWithoutExtension(sourceFileName),
                    SourceFileName = sourceFileName,
                    SourcePath = request.FilePath,
                    FileHash = fileHash,
                    Kind = snapshot.Kind,
                    ImportedAt = DateTimeOffset.UtcNow,
                    IsMaster = true
                };
                _db.ArchiveWorkbooks.Add(workbook);
            }

            var isConflict = request.BaseVersion.HasValue
                             && workbook.Version > request.BaseVersion.Value;

            var existingYears = await _db.ArchiveYears
                .Include(year => year.Dossiers)
                .ThenInclude(dossier => dossier.Documents)
                .Where(year => year.WorkbookId == workbook.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            var changes = new List<SyncChange>();
            var added = 0;
            var updated = 0;
            var deleted = 0;
            var conflicts = 0;
            var snapshotYears = snapshot.Years
                .Select(year => year.Year)
                .ToHashSet();

            foreach (var yearData in snapshot.Years.OrderBy(year => year.Year))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var year = existingYears.FirstOrDefault(item => item.Year == yearData.Year);
                if (year is null)
                {
                    year = new ArchiveYear
                    {
                        WorkbookId = workbook.Id,
                        Year = yearData.Year,
                        Workbook = workbook
                    };
                    _db.ArchiveYears.Add(year);
                    existingYears.Add(year);
                }

                year.SheetName = string.IsNullOrWhiteSpace(yearData.SheetName)
                    ? yearData.Year.ToString(CultureInfo.InvariantCulture)
                    : yearData.SheetName;
                year.SourceSheets = yearData.SourceSheets.Count > 0
                    ? string.Join(", ", yearData.SourceSheets)
                    : null;
                year.SortOrder = yearData.Year;

                var existingByKey = year.Dossiers
                    .GroupBy(dossier => NormalizeKey(dossier.NumberLabel, dossier.Number))
                    .ToDictionary(
                        group => group.Key,
                        group => new Queue<Dossier>(group.OrderBy(dossier => dossier.MlRow)),
                        StringComparer.OrdinalIgnoreCase);

                foreach (var incoming in yearData.Dossiers.OrderBy(dossier => dossier.MlRow))
                {
                    var key = NormalizeKey(incoming.NumberLabel, incoming.Number);
                    Dossier? dossier = null;
                    if (existingByKey.TryGetValue(key, out var queue) && queue.Count > 0)
                    {
                        dossier = queue.Dequeue();
                    }

                    if (dossier is null)
                    {
                        dossier = new Dossier
                        {
                            WorkbookId = workbook.Id,
                            Workbook = workbook,
                            YearId = year.Id,
                            Year = year,
                            CreatedAt = now
                        };
                        year.Dossiers.Add(dossier);
                        // Entity mới đã có Guid sẵn; thêm trực tiếp vào DbSet để EF
                        // chắc chắn đánh dấu Added thay vì suy đoán là entity cũ.
                        _db.Dossiers.Add(dossier);
                        ApplyIncoming(dossier, incoming, now);
                        UpdateDocuments(dossier, incoming);
                        added++;
                        continue;
                    }

                    var before = ToJson(dossier);
                    ApplyIncoming(dossier, incoming, now);
                    UpdateDocuments(dossier, incoming);
                    var after = ToJson(dossier);

                    if (!string.Equals(before, after, StringComparison.Ordinal))
                    {
                        updated++;
                        if (isConflict)
                        {
                            conflicts++;
                        }

                        changes.Add(new SyncChange
                        {
                            EntityType = "Dossier",
                            EntityKey = $"{yearData.Year}|{key}",
                            ChangeType = "Updated",
                            OldValueJson = before,
                            NewValueJson = after,
                            IsConflict = isConflict,
                            Resolution = isConflict ? "LastWriteWins" : null
                        });
                    }
                }

                foreach (var queue in existingByKey.Values)
                {
                    while (queue.Count > 0)
                    {
                        var dossier = queue.Dequeue();
                        deleted++;
                        changes.Add(new SyncChange
                        {
                            EntityType = "Dossier",
                            EntityKey = $"{yearData.Year}|{NormalizeKey(dossier.NumberLabel, dossier.Number)}",
                            ChangeType = "Deleted",
                            OldValueJson = ToJson(dossier),
                            IsConflict = isConflict,
                            Resolution = isConflict ? "LastWriteWins" : null
                        });
                        _db.Dossiers.Remove(dossier);
                    }
                }
            }

            foreach (var removedYear in existingYears
                         .Where(year => !snapshotYears.Contains(year.Year))
                         .ToList())
            {
                foreach (var dossier in removedYear.Dossiers)
                {
                    deleted++;
                    changes.Add(new SyncChange
                    {
                        EntityType = "Dossier",
                        EntityKey = $"{removedYear.Year}|{NormalizeKey(dossier.NumberLabel, dossier.Number)}",
                        ChangeType = "Deleted",
                        OldValueJson = ToJson(dossier),
                        IsConflict = isConflict,
                        Resolution = isConflict ? "LastWriteWins" : null
                    });
                }

                _db.ArchiveYears.Remove(removedYear);
            }

            workbook.SourceFileName = sourceFileName;
            workbook.SourcePath = request.FilePath;
            workbook.FileHash = fileHash;
            workbook.Kind = snapshot.Kind;
            workbook.LastSyncedAt = now;
            workbook.Version++;

            var session = new SyncSession
            {
                WorkbookId = workbook.Id,
                Workbook = workbook,
                SourceFileName = sourceFileName,
                UploadedAt = now,
                Status = SyncStatus.Applied,
                AddedCount = added,
                UpdatedCount = updated,
                DeletedCount = deleted,
                ConflictCount = conflicts,
                Changes = changes
            };
            _db.SyncSessions.Add(session);

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Đồng bộ {File}: +{Added} ~{Updated} -{Deleted}, {Conflicts} xung đột",
                sourceFileName,
                added,
                updated,
                deleted,
                conflicts);

            return new SyncOutcome
            {
                Success = true,
                SyncSessionId = session.Id,
                WorkbookId = workbook.Id,
                AddedCount = added,
                UpdatedCount = updated,
                DeletedCount = deleted,
                ConflictCount = conflicts
            };
        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Đồng bộ {File} thất bại", sourceFileName);

            var detailedError = ex.InnerException is not null
                ? $"{ex.Message} --> {ex.InnerException.Message}"
                : ex.Message;

            var failed = new SyncSession
            {
                SourceFileName = sourceFileName,
                UploadedAt = DateTimeOffset.UtcNow,
                Status = SyncStatus.Failed,
                ErrorMessage = Truncate(detailedError, 2000)
            };

            try
            {
                _db.SyncSessions.Add(failed);
                await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception saveException)
            {
                _logger.LogError(saveException, "Không ghi được SyncSession thất bại");
            }

            return new SyncOutcome
            {
                Success = false,
                SyncSessionId = failed.Id,
                ErrorMessage = detailedError
            };
        }
    }

    private async Task<ArchiveWorkbook?> FindWorkbookAsync(
        Guid? workbookId,
        string sourceFileName,
        string fileHash,
        CancellationToken cancellationToken)
    {
        if (workbookId.HasValue)
        {
            return await _db.ArchiveWorkbooks
                .FirstOrDefaultAsync(
                    workbook => workbook.Id == workbookId.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var byHash = await _db.ArchiveWorkbooks
            .OrderByDescending(workbook => workbook.ImportedAt)
            .FirstOrDefaultAsync(
                workbook => workbook.FileHash == fileHash,
                cancellationToken)
            .ConfigureAwait(false);
        if (byHash is not null)
        {
            return byHash;
        }

        return await _db.ArchiveWorkbooks
            .Where(workbook => workbook.SourceFileName == sourceFileName)
            .OrderByDescending(workbook => workbook.ImportedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ApplyIncoming(
        Dossier dossier,
        DossierRecord incoming,
        DateTimeOffset now)
    {
        dossier.MlRow = incoming.MlRow;
        dossier.Number = incoming.Number;
        dossier.NumberLabel = Truncate(string.IsNullOrWhiteSpace(incoming.NumberLabel)
            ? incoming.Number.ToString(CultureInfo.InvariantCulture)
            : incoming.NumberLabel, 100);
        dossier.Title = Truncate(string.IsNullOrWhiteSpace(incoming.Title)
            ? "MỤC LỤC VĂN BẢN"
            : incoming.Title, 300);
        dossier.Group = Truncate(incoming.Group, 300);
        dossier.Type = Truncate(incoming.Type, 300);
        dossier.BoxNumber = Truncate(incoming.BoxNumber, 100);
        dossier.SubNumber = Truncate(incoming.SubNumber, 100);
        dossier.OriginalSummary = Truncate(incoming.OriginalSummary, 2000);
        dossier.NumberNote = Truncate(incoming.NumberNote, 500);
        dossier.SourceVolume = Truncate(incoming.SourceVolume, 100);
        dossier.HasPrintLayout = incoming.HasPrintLayout;
        dossier.IsAggregate = incoming.IsAggregate;
        dossier.TotalPages = incoming.TotalPages;
        dossier.UpdatedAt = now;
    }

    private void UpdateDocuments(Dossier dossier, DossierRecord incoming)
    {
        var documents = dossier.Documents;
        var incomingDocuments = incoming.Documents;

        for (var index = documents.Count - 1; index >= incomingDocuments.Count; index--)
        {
            documents.RemoveAt(index);
        }

        for (var index = 0; index < incomingDocuments.Count; index++)
        {
            var source = incomingDocuments[index];
            if (index < documents.Count)
            {
                var target = documents[index];
                target.Stt = source.Stt;
                target.SoKyHieu = Truncate(source.SoKyHieu, 200);
                target.Ngay = Truncate(source.Ngay, 100);
                target.TrichYeu = Truncate(source.TrichYeu, 2000);
                target.TacGia = Truncate(source.TacGia, 300);
                target.ToSo = Truncate(source.ToSo, 100);
                target.GhiChu = Truncate(source.GhiChu, 4000);
            }
            else
            {
                var line = new DocumentLine
                {
                    Dossier = dossier,
                    DossierId = dossier.Id,
                    Stt = source.Stt,
                    SoKyHieu = Truncate(source.SoKyHieu, 200),
                    Ngay = Truncate(source.Ngay, 100),
                    TrichYeu = Truncate(source.TrichYeu, 2000),
                    TacGia = Truncate(source.TacGia, 300),
                    ToSo = Truncate(source.ToSo, 100),
                    GhiChu = Truncate(source.GhiChu, 4000)
                };
                documents.Add(line);
                _db.DocumentLines.Add(line);
            }
        }
    }

    private static string NormalizeKey(string? numberLabel, int number)
    {
        return string.IsNullOrWhiteSpace(numberLabel)
            ? number.ToString(CultureInfo.InvariantCulture)
            : numberLabel.Trim().ToUpperInvariant();
    }

    private static string ToJson(Dossier dossier)
    {
        return JsonSerializer.Serialize(new
        {
            dossier.MlRow,
            dossier.Number,
            dossier.NumberLabel,
            dossier.Title,
            dossier.Group,
            dossier.Type,
            dossier.BoxNumber,
            dossier.SubNumber,
            dossier.OriginalSummary,
            dossier.NumberNote,
            dossier.SourceVolume,
            dossier.HasPrintLayout,
            dossier.IsAggregate,
            dossier.TotalPages,
            Documents = dossier.Documents.Select(document => new
            {
                document.Stt,
                document.SoKyHieu,
                document.Ngay,
                document.TrichYeu,
                document.TacGia,
                document.ToSo,
                document.GhiChu
            }).ToList()
        }, JsonOptions);
    }

    private static async Task<string> ComputeHashAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Truncate(string? value, int length) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= length ? value : value[..length]);
}
