using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddGroomingVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroomingVisits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateVersion = table.Column<int>(type: "int", nullable: false),
                    StartedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EstimateSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingVisits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroomingVisits_AspNetUsers_StartedByEmployeeId",
                        column: x => x.StartedByEmployeeId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroomingVisits_GroomingQuoteRevisions_EstimateId_EstimateVersion",
                        columns: x => new { x.EstimateId, x.EstimateVersion },
                        principalTable: "GroomingQuoteRevisions",
                        principalColumns: new[] { "QuoteId", "Number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroomingVisits_EstimateId",
                table: "GroomingVisits",
                column: "EstimateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroomingVisits_EstimateId_EstimateVersion",
                table: "GroomingVisits",
                columns: new[] { "EstimateId", "EstimateVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_GroomingVisits_StartedByEmployeeId",
                table: "GroomingVisits",
                column: "StartedByEmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroomingVisits");
        }
    }
}
