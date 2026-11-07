using System.Text.Json;

namespace Stp.Core.Jobs;

public enum OriginalFileMode
{
    /// <summary>Giữ file upload gốc; ghi kết quả vào thư mục outputs của job.</summary>
    Copy = 0,

    /// <summary>Tạo file .xlsm cạnh file upload và sao lưu file gốc vào data/backups.</summary>
    InPlaceWithBackup = 1,

    /// <summary>Ghi tất cả kết quả vào thư mục outputs của job (mặc định).</summary>
    OutputFolder = 2
}

/// <summary>Tùy chọn lưu trong BatchJob.OptionsJson.</summary>
public sealed class ConversionJobOptions
{
    public OriginalFileMode OriginalFileMode { get; set; } = OriginalFileMode.Copy;

    /// <summary>Đóng gói các file .xlsm thành một ZIP.</summary>
    public bool CreateZip { get; set; } = true;

    /// <summary>Tạo thêm một workbook gộp từ tất cả file đầu vào.</summary>
    public bool MergeOutput { get; set; } = true;

    /// <summary>Chỉ tạo workbook gộp, không chuyển từng file.</summary>
    public bool MergeOnly { get; set; }

    /// <summary>Sinh sheet in cho file loại 2 trước khi chuyển.</summary>
    public bool GeneratePrintSheets { get; set; } = true;

    public bool InjectMacro { get; set; } = true;

    public string? HeaderText { get; set; }

    public string? FooterText { get; set; }

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static ConversionJobOptions FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ConversionJobOptions();
        }

        try
        {
            return JsonSerializer.Deserialize<ConversionJobOptions>(json, SerializerOptions)
                   ?? new ConversionJobOptions();
        }
        catch (JsonException)
        {
            return new ConversionJobOptions();
        }
    }
}

public interface IBatchJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}

public interface IBatchJobProcessor
{
    Task ProcessAsync(Guid jobId, CancellationToken cancellationToken);
}
