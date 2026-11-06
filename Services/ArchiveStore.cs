using System.Text.Encodings.Web;
using System.Text.Json;
using StpWeb.Models;

namespace StpWeb.Services;

/// <summary>
/// Lưu dữ liệu đã import vào App_Data/archive.json để không phải import lại mỗi lần mở web.
/// </summary>
public sealed class ArchiveStore
{
    private readonly string _filePath;
    private readonly ILogger<ArchiveStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ArchiveData? _cache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public ArchiveStore(IWebHostEnvironment environment, ILogger<ArchiveStore> logger)
    {
        _logger = logger;
        var dataDir = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "archive.json");
    }

    public async Task<ArchiveData> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is not null)
            {
                return _cache;
            }

            if (!File.Exists(_filePath))
            {
                _cache = new ArchiveData();
                return _cache;
            }

            try
            {
                await using var stream = File.OpenRead(_filePath);
                _cache = await JsonSerializer.DeserializeAsync<ArchiveData>(stream, JsonOptions, cancellationToken)
                         ?? new ArchiveData();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không đọc được dữ liệu đã lưu tại {Path}", _filePath);
                _cache = new ArchiveData();
            }

            return _cache;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ArchiveData data, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tempPath = _filePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, _filePath, overwrite: true);
            _cache = data;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }

            _cache = new ArchiveData();
        }
        finally
        {
            _gate.Release();
        }
    }
}
