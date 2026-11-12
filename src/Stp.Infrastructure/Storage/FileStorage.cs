using System.Text;
using Microsoft.Extensions.Options;
using Stp.Core.Storage;

namespace Stp.Infrastructure.Storage;

/// <summary>
/// Lưu file trong data/uploads, data/outputs, data/backups. Mọi tên file được
/// chuẩn hoá và mọi đường dẫn được kiểm tra nằm dưới RootPath.
/// </summary>
public sealed class FileStorage : IFileStorage
{
    private static readonly char[] InvalidFileNameChars =
        Path.GetInvalidFileNameChars().Concat(new[] { ':', '*', '?', '"', '<', '>', '|' }).Distinct().ToArray();

    private readonly StorageOptions _options;
    private readonly string _rootPath;

    public FileStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        _rootPath = Path.GetFullPath(_options.RootPath);

        Directory.CreateDirectory(_rootPath);
        Directory.CreateDirectory(Path.Combine(_rootPath, "uploads"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "outputs"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "backups"));
    }

    public string RootPath => _rootPath;

    public async Task<StoredFile> SaveUploadAsync(
        Stream content,
        string fileName,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var directory = GetJobUploadDirectory(jobId);
        Directory.CreateDirectory(directory);

        var safeName = SanitizeFileName(fileName);
        EnsureAllowedExtension(safeName);

        var path = GetUniquePath(directory, safeName);
        await using (var target = new FileStream(
                         path,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 81920,
                         useAsync: true))
        {
            await content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);

            if (target.Length == 0)
            {
                target.Close();
                TryDelete(path);
                throw new InvalidDataException("File upload rỗng.");
            }

            if (target.Length > _options.MaxUploadBytes)
            {
                target.Close();
                TryDelete(path);
                throw new InvalidDataException(
                    $"File vượt quá giới hạn {_options.MaxUploadBytes / (1024 * 1024)} MB.");
            }
        }

        return new StoredFile(Path.GetFileName(path), path, new FileInfo(path).Length);
    }

    public async Task<StoredFile> BackupAsync(
        string sourcePath,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Không tìm thấy file cần sao lưu.", sourcePath);
        }

        if (!IsUnderRoot(sourcePath))
        {
            throw new InvalidOperationException("Chỉ được sao lưu file nằm trong thư mục dữ liệu.");
        }

        var directory = GetJobBackupDirectory(jobId);
        Directory.CreateDirectory(directory);

        var path = GetUniquePath(directory, Path.GetFileName(sourcePath));
        await using (var source = new FileStream(
                         sourcePath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         bufferSize: 81920,
                         useAsync: true))
        await using (var target = new FileStream(
                         path,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 81920,
                         useAsync: true))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        return new StoredFile(Path.GetFileName(path), path, new FileInfo(path).Length);
    }

    public string GetJobUploadDirectory(Guid jobId) =>
        Path.Combine(_rootPath, "uploads", jobId.ToString("N"));

    public string GetJobOutputDirectory(Guid jobId) =>
        Path.Combine(_rootPath, "outputs", jobId.ToString("N"));

    public string GetJobBackupDirectory(Guid jobId) =>
        Path.Combine(_rootPath, "backups", jobId.ToString("N"));

    public string GetUniquePath(string directory, string fileName)
    {
        var safeDirectory = Path.GetFullPath(directory);
        if (!IsUnderRoot(safeDirectory))
        {
            throw new InvalidOperationException("Thư mục đích nằm ngoài thư mục dữ liệu.");
        }

        Directory.CreateDirectory(safeDirectory);

        var safeName = SanitizeFileName(fileName);
        var candidate = Path.Combine(safeDirectory, safeName);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var index = 2; index < 1000; index++)
        {
            candidate = Path.Combine(safeDirectory, $"{baseName} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Không tạo được tên file duy nhất.");
    }

    public bool IsUnderRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        return fullPath.Equals(_rootPath, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(
                   _rootPath + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    public void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path) || !IsUnderRoot(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (fullPath.Equals(_rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.Delete(fullPath, recursive: true);
    }

    private void EnsureAllowedExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var allowed = _options.AllowedExtensions
            .Any(value => string.Equals(value, extension, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            throw new NotSupportedException(
                $"Định dạng '{extension}' chưa được hỗ trợ. " +
                $"Chỉ nhận: {string.Join(", ", _options.AllowedExtensions)}.");
        }
    }

    public static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return "file";
        }

        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(
                character < 32 || InvalidFileNameChars.Contains(character)
                    ? '_'
                    : character);
        }

        name = builder.ToString().Trim().TrimEnd('.');
        if (name.Length == 0)
        {
            return "file";
        }

        var extension = Path.GetExtension(name);
        var baseName = Path.GetFileNameWithoutExtension(name);
        if (baseName.Length > 180)
        {
            baseName = baseName[..180];
            name = baseName + extension;
        }

        return name;
    }

    public int SweepOldArtifacts(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var count = 0;
        var targets = new[]
        {
            Path.Combine(_rootPath, "uploads"),
            Path.Combine(_rootPath, "outputs"),
            Path.Combine(_rootPath, "backups")
        };

        foreach (var dir in targets)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var subDir in Directory.GetDirectories(dir))
            {
                try
                {
                    var info = new DirectoryInfo(subDir);
                    if (info.LastWriteTimeUtc < cutoff)
                    {
                        Directory.Delete(subDir, recursive: true);
                        count++;
                    }
                }
                catch
                {
                    // Bỏ qua nếu thư mục đang bị khoá hoặc lỗi xoá
                }
            }

            foreach (var file in Directory.GetFiles(dir))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTimeUtc < cutoff)
                    {
                        File.Delete(file);
                        count++;
                    }
                }
                catch
                {
                    // Bỏ qua file đang dùng
                }
            }
        }

        return count;
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
            // Bỏ qua file tạm không xoá được.
        }
    }
}
