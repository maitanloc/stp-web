using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceMemories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VoiceMemories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RawSpeech = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CleanSpeech = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Command = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SheetNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TargetRowNumber = table.Column<int>(type: "int", nullable: true),
                    TargetDocumentNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UsageCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceMemories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VoiceMemories_CleanSpeech",
                table: "VoiceMemories",
                column: "CleanSpeech");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceMemories_LastUsedAt",
                table: "VoiceMemories",
                column: "LastUsedAt");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceMemories_UsageCount",
                table: "VoiceMemories",
                column: "UsageCount");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VoiceMemories");
        }
    }
}
