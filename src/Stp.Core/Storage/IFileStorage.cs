namespace Stp.Core.Storage;

/// <summary>
/// Cấu hình lưu file runtime. Mọi đường dẫn API trả ra đều nằm dưới <see cref="RootPath"/>.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>Thư mục gốc; đường dẫn tương đối được tính từ content root của API.</summary>
    public string RootPath { get; set; } = "data";

    /// <summary>Giới hạn kích thước một file upload (byte).</summary>
    public long MaxUploadBytes { get; set; } = 200L * 1024 * 1024;

    /// <summary>Số file tối đa trong một job.</summary>
    public int MaxFilesPerJob { get; set; } = 50;

    public string[] AllowedExtensions { get; set; } =
    {
        ".xlsx", ".xlsm", ".xltx", ".xltm"
    };
}

public sealed record StoredFile(string FileName, string FullPath, long SizeBytes);

/// <summary>
/// Lưu file upload/output/backup trong thư mục data, chống path traversal.
/// </summary>
public interface IFileStorage
{
    string RootPath { get; }

    /// <summary>Lưu file upload vào thư mục riêng của job.</summary>
    Task<StoredFile> SaveUploadAsync(
        Stream content,
        string fileName,
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>Sao lưu file gốc trước khi ghi đè tại chỗ.</summary>
    Task<StoredFile> BackupAsync(
        string sourcePath,
        Guid jobId,
        CancellationToken cancellationToken = default);

    string GetJobUploadDirectory(Guid jobId);

    string GetJobOutputDirectory(Guid jobId);

    string GetJobBackupDirectory(Guid jobId);

    /// <summary>Trả về đường dẫn chưa tồn tại trong thư mục, giữ tên file an toàn.</summary>
    string GetUniquePath(string directory, string fileName);

    bool IsUnderRoot(string path);

    void DeleteDirectory(string path);

    /// <summary>
    /// L1: Quét dọn các tệp/thư mục tạm (uploads, outputs, backups) cũ hơn khoảng thời gian chỉ định (mặc định 30 ngày).
    /// Giữ lại avatars và các thư mục cấu hình hệ thống.
    /// </summary>
    int SweepOldArtifacts(TimeSpan maxAge);
}
