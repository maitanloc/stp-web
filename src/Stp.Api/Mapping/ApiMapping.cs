using Stp.Contracts;
using Stp.Core.Domain;
using Stp.Core.Excel;
using Stp.Core.Jobs;

namespace Stp.Api.Mapping;

/// <summary>Chuyển domain/inspection sang DTO trả về SPA.</summary>
internal static class ApiMapping
{
    public static InspectionResponse ToResponse(this WorkbookInspection inspection) =>
        new(
            inspection.FileName,
            inspection.FileSizeBytes,
            inspection.Kind.ToString(),
            inspection.HasMlSheet,
            inspection.MlSheetName,
            inspection.HasPrintSheets,
            inspection.HasMacro,
            inspection.SheetCount,
            inspection.YearCount,
            inspection.DossierCount,
            inspection.PrintReadyCount,
            inspection.TotalPages,
            inspection.Types,
            inspection.Years
                .Select(year => new YearInspectionResponse(
                    year.Year,
                    year.SheetName,
                    year.DossierCount,
                    year.MinNumber,
                    year.MaxNumber,
                    year.HasPrintSheet,
                    year.SourceSheets))
                .ToList(),
            inspection.Sheets
                .Select(sheet => new SheetInspectionResponse(
                    sheet.Name,
                    sheet.IsMl,
                    sheet.IsPrint,
                    sheet.Year,
                    sheet.LastRow,
                    sheet.LastColumn,
                    sheet.BlockCount))
                .ToList(),
            inspection.Warnings);

    public static ConversionOptionsResponse ToResponse(this ConversionJobOptions options) =>
        new(
            options.OriginalFileMode.ToString(),
            options.CreateZip,
            options.MergeOutput,
            options.MergeOnly,
            options.GeneratePrintSheets,
            options.InjectMacro,
            options.HeaderText,
            options.FooterText);

    public static JobSummaryResponse ToSummary(this BatchJob job) =>
        new(
            job.Id,
            job.Type.ToString(),
            job.Status.ToString(),
            job.TotalItems,
            job.SucceededItems,
            job.FailedItems,
            job.ErrorMessage,
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.OutputPath is null ? null : Path.GetFileName(job.OutputPath),
            job.OutputPath is not null);

    public static JobItemResponse ToResponse(this BatchJobItem item) =>
        new(
            item.Id,
            item.InputFileName,
            item.Status.ToString(),
            item.WorkbookKind.ToString(),
            item.DossierCount,
            item.ErrorMessage,
            item.StartedAt,
            item.CompletedAt,
            item.OutputPath is not null);

    public static JobLogResponse ToResponse(this JobLog log) =>
        new(log.Timestamp, log.Level, log.Message, log.Details);

    public static DossierSummaryResponse ToSummary(this Dossier dossier) =>
        new(
            dossier.Id,
            dossier.Year?.Year ?? 0,
            dossier.MlRow,
            dossier.Number,
            dossier.NumberLabel,
            dossier.Type,
            dossier.Group,
            dossier.BoxNumber,
            dossier.OriginalSummary,
            dossier.HasPrintLayout,
            dossier.IsAggregate,
            dossier.TotalPages,
            dossier.Documents.Count);

    public static DossierResponse ToResponse(this Dossier dossier) =>
        new(
            dossier.Id,
            dossier.WorkbookId,
            dossier.Year?.Year ?? 0,
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
            dossier.FullText,
            dossier.Note,
            dossier.RowVersion.Length == 0 ? string.Empty : Convert.ToBase64String(dossier.RowVersion),
            dossier.UpdatedAt,
            dossier.Documents
                .OrderBy(line => line.Stt)
                .Select(line => new DocumentLineResponse(
                    line.Stt,
                    line.SoKyHieu,
                    line.Ngay,
                    line.TrichYeu,
                    line.TacGia,
                    line.ToSo,
                    line.GhiChu))
                .ToList());
}
