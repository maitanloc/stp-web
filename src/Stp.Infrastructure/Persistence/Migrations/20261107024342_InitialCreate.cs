using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchiveWorkbooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SourcePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    FileHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsMaster = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveWorkbooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BatchJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalItems = table.Column<int>(type: "int", nullable: false),
                    SucceededItems = table.Column<int>(type: "int", nullable: false),
                    FailedItems = table.Column<int>(type: "int", nullable: false),
                    OutputPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MacroVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ChangeNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ParentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MacroVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MacroVersions_MacroVersions_ParentVersionId",
                        column: x => x.ParentVersionId,
                        principalTable: "MacroVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrintPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PaperSize = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Orientation = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MarginLeft = table.Column<double>(type: "float", nullable: false),
                    MarginRight = table.Column<double>(type: "float", nullable: false),
                    MarginTop = table.Column<double>(type: "float", nullable: false),
                    MarginBottom = table.Column<double>(type: "float", nullable: false),
                    MarginHeader = table.Column<double>(type: "float", nullable: false),
                    MarginFooter = table.Column<double>(type: "float", nullable: false),
                    Scale = table.Column<int>(type: "int", nullable: false),
                    FitToWidth = table.Column<bool>(type: "bit", nullable: false),
                    FitToHeight = table.Column<bool>(type: "bit", nullable: false),
                    FontFamily = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FontSize = table.Column<double>(type: "float", nullable: false),
                    HeaderText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FooterText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Watermark = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintPresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArchiveYears",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkbookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    SheetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceSheets = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveYears", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchiveYears_ArchiveWorkbooks_WorkbookId",
                        column: x => x.WorkbookId,
                        principalTable: "ArchiveWorkbooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkbookId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceFileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AddedCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false),
                    DeletedCount = table.Column<int>(type: "int", nullable: false),
                    ConflictCount = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncSessions_ArchiveWorkbooks_WorkbookId",
                        column: x => x.WorkbookId,
                        principalTable: "ArchiveWorkbooks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BatchJobItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputFileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    InputPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OutputPath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WorkbookKind = table.Column<int>(type: "int", nullable: false),
                    DossierCount = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchJobItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchJobItems_BatchJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "BatchJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobLogs_BatchJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "BatchJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Dossiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkbookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    YearId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MlRow = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    NumberLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Group = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BoxNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OriginalSummary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    NumberNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SourceVolume = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HasPrintLayout = table.Column<bool>(type: "bit", nullable: false),
                    IsAggregate = table.Column<bool>(type: "bit", nullable: false),
                    TotalPages = table.Column<int>(type: "int", nullable: false),
                    FullText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dossiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dossiers_ArchiveWorkbooks_WorkbookId",
                        column: x => x.WorkbookId,
                        principalTable: "ArchiveWorkbooks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Dossiers_ArchiveYears_YearId",
                        column: x => x.YearId,
                        principalTable: "ArchiveYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SyncSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OldValueJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValueJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsConflict = table.Column<bool>(type: "bit", nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncChanges_SyncSessions_SyncSessionId",
                        column: x => x.SyncSessionId,
                        principalTable: "SyncSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DossierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stt = table.Column<int>(type: "int", nullable: false),
                    SoKyHieu = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Ngay = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TrichYeu = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    TacGia = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ToSo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    GhiChu = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentLines_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveWorkbooks_FileHash",
                table: "ArchiveWorkbooks",
                column: "FileHash");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveWorkbooks_Name",
                table: "ArchiveWorkbooks",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveYears_WorkbookId_Year",
                table: "ArchiveYears",
                columns: new[] { "WorkbookId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveYears_Year",
                table: "ArchiveYears",
                column: "Year");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_BatchJobItems_JobId",
                table: "BatchJobItems",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchJobs_CreatedAt",
                table: "BatchJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BatchJobs_Status",
                table: "BatchJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLines_DossierId",
                table: "DocumentLines",
                column: "DossierId");

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_Type",
                table: "Dossiers",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_UpdatedAt",
                table: "Dossiers",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_WorkbookId_MlRow",
                table: "Dossiers",
                columns: new[] { "WorkbookId", "MlRow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_YearId_Number",
                table: "Dossiers",
                columns: new[] { "YearId", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_JobLogs_JobId_Timestamp",
                table: "JobLogs",
                columns: new[] { "JobId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_MacroVersions_IsActive",
                table: "MacroVersions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_MacroVersions_Name_VersionNumber",
                table: "MacroVersions",
                columns: new[] { "Name", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MacroVersions_ParentVersionId",
                table: "MacroVersions",
                column: "ParentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintPresets_Name",
                table: "PrintPresets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_IsConflict",
                table: "SyncChanges",
                column: "IsConflict");

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_SyncSessionId",
                table: "SyncChanges",
                column: "SyncSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncSessions_Status",
                table: "SyncSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SyncSessions_UploadedAt",
                table: "SyncSessions",
                column: "UploadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SyncSessions_WorkbookId",
                table: "SyncSessions",
                column: "WorkbookId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BatchJobItems");

            migrationBuilder.DropTable(
                name: "DocumentLines");

            migrationBuilder.DropTable(
                name: "JobLogs");

            migrationBuilder.DropTable(
                name: "MacroVersions");

            migrationBuilder.DropTable(
                name: "PrintPresets");

            migrationBuilder.DropTable(
                name: "SyncChanges");

            migrationBuilder.DropTable(
                name: "Dossiers");

            migrationBuilder.DropTable(
                name: "BatchJobs");

            migrationBuilder.DropTable(
                name: "SyncSessions");

            migrationBuilder.DropTable(
                name: "ArchiveYears");

            migrationBuilder.DropTable(
                name: "ArchiveWorkbooks");
        }
    }
}
