using Microsoft.Extensions.Options;
using Stp.Core.Storage;
using Stp.Infrastructure.Storage;

namespace Stp.Tests.Storage;

public sealed class FileStorageTests
{
    [Fact]
    public async Task SaveUploadAsync_WritesFileAndReturnsPath()
    {
        var directory = CreateTempDirectory();
        try
        {
            var storage = CreateStorage(directory);
            await using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

            var stored = await storage.SaveUploadAsync(
                stream,
                "  DK KET HON:2024?.xlsx  ",
                Guid.NewGuid());

            Assert.True(File.Exists(stored.FullPath));
            Assert.True(storage.IsUnderRoot(stored.FullPath));
            Assert.Equal(4, stored.SizeBytes);
            Assert.DoesNotContain(':', Path.GetFileName(stored.FullPath));
            Assert.DoesNotContain('?', Path.GetFileName(stored.FullPath));
            Assert.EndsWith(".xlsx", stored.FileName);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public async Task SaveUploadAsync_RejectsUnsupportedExtension()
    {
        var directory = CreateTempDirectory();
        try
        {
            var storage = CreateStorage(directory);
            await using var stream = new MemoryStream(new byte[] { 1 });

            await Assert.ThrowsAsync<NotSupportedException>(() =>
                storage.SaveUploadAsync(stream, "virus.exe", Guid.NewGuid()));
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public async Task SaveUploadAsync_RejectsEmptyFile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var storage = CreateStorage(directory);
            await using var stream = new MemoryStream();

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                storage.SaveUploadAsync(stream, "empty.xlsx", Guid.NewGuid()));
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public async Task SaveUploadAsync_RejectsFileLargerThanLimit()
    {
        var directory = CreateTempDirectory();
        try
        {
            var storage = CreateStorage(directory, maxUploadBytes: 4);
            await using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                storage.SaveUploadAsync(stream, "big.xlsx", Guid.NewGuid()));
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public void GetUniquePath_NeverOverwritesExistingFile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var storage = CreateStorage(directory);
            var jobId = Guid.NewGuid();
            var outputDirectory = storage.GetJobOutputDirectory(jobId);
            Directory.CreateDirectory(outputDirectory);

            var first = storage.GetUniquePath(outputDirectory, "STP.xlsm");
            File.WriteAllText(first, "first");

            var second = storage.GetUniquePath(outputDirectory, "STP.xlsm");
            Assert.NotEqual(first, second);
            Assert.Contains("STP (2)", second);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    [Fact]
    public async Task BackupAsync_RejectsFileOutsideRoot()
    {
        var directory = CreateTempDirectory();
        var outside = CreateTempDirectory();
        try
        {
            var storage = CreateStorage(directory);
            var outsideFile = Path.Combine(outside, "outside.xlsx");
            await File.WriteAllTextAsync(outsideFile, "x");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                storage.BackupAsync(outsideFile, Guid.NewGuid()));
        }
        finally
        {
            TryDelete(directory);
            TryDelete(outside);
        }
    }

    [Fact]
    public void SanitizeFileName_KeepsVietnameseCharacters()
    {
        var safe = FileStorage.SanitizeFileName("Hồ sơ 2024.xlsx");

        Assert.Equal("Hồ sơ 2024.xlsx", safe);
    }

    [Fact]
    public void SweepOldArtifacts_DeletesOldFilesAndDirectories()
    {
        var directory = CreateTempDirectory();
        try
        {
            var storage = CreateStorage(directory);
            var uploadDir = Path.Combine(directory, "uploads", "old_job");
            Directory.CreateDirectory(uploadDir);
            var oldFile = Path.Combine(uploadDir, "data.xlsx");
            File.WriteAllText(oldFile, "old content");
            Directory.SetLastWriteTimeUtc(uploadDir, DateTime.UtcNow.AddDays(-35));
            File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-35));

            var newUploadDir = Path.Combine(directory, "uploads", "new_job");
            Directory.CreateDirectory(newUploadDir);
            var newFile = Path.Combine(newUploadDir, "data2.xlsx");
            File.WriteAllText(newFile, "new content");

            var swept = storage.SweepOldArtifacts(TimeSpan.FromDays(30));

            Assert.True(swept >= 1);
            Assert.False(Directory.Exists(uploadDir));
            Assert.True(Directory.Exists(newUploadDir));
        }
        finally
        {
            TryDelete(directory);
        }
    }

    private static FileStorage CreateStorage(string root, long maxUploadBytes = 1024 * 1024)
    {
        return new FileStorage(Options.Create(new StorageOptions
        {
            RootPath = root,
            MaxUploadBytes = maxUploadBytes,
            AllowedExtensions = new[] { ".xlsx", ".xlsm" }
        }));
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "stp-storage-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Bỏ qua thư mục tạm còn file đang mở.
        }
    }
}
