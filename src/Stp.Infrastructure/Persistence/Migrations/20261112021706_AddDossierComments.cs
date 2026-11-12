using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Stp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDossierComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DossierComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DossierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DossierComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DossierComments_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_EntityKey",
                table: "SyncChanges",
                column: "EntityKey");

            migrationBuilder.CreateIndex(
                name: "IX_DossierComments_CreatedAt",
                table: "DossierComments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DossierComments_DossierId",
                table: "DossierComments",
                column: "DossierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DossierComments");

            migrationBuilder.DropIndex(
                name: "IX_SyncChanges_EntityKey",
                table: "SyncChanges");
        }
    }
}
