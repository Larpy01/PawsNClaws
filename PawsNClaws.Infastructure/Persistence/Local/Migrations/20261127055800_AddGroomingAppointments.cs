using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddGroomingAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroomingAppointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateVersion = table.Column<int>(type: "int", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScheduledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingAppointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroomingAppointments_AspNetUsers_GroomerId",
                        column: x => x.GroomerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroomingAppointments_GroomingQuoteRevisions_EstimateId_EstimateVersion",
                        columns: x => new { x.EstimateId, x.EstimateVersion },
                        principalTable: "GroomingQuoteRevisions",
                        principalColumns: new[] { "QuoteId", "Number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroomingAppointments_PetRecords_PetId",
                        column: x => x.PetId,
                        principalTable: "PetRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroomingAppointments_EstimateId",
                table: "GroomingAppointments",
                column: "EstimateId",
                unique: true,
                filter: "[CancelledAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GroomingAppointments_EstimateId_EstimateVersion",
                table: "GroomingAppointments",
                columns: new[] { "EstimateId", "EstimateVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_GroomingAppointments_GroomerId_StartsAtUtc",
                table: "GroomingAppointments",
                columns: new[] { "GroomerId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GroomingAppointments_PetId_StartsAtUtc",
                table: "GroomingAppointments",
                columns: new[] { "PetId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GroomingAppointments_StartsAtUtc",
                table: "GroomingAppointments",
                column: "StartsAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroomingAppointments");
        }
    }
}
