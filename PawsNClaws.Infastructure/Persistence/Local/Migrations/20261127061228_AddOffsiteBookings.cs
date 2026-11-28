using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddOffsiteBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OffsiteBookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateVersion = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TransportEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransportStartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TransportEndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TransportScheduledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustodyAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CustodyRecordedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReleasedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryRecordedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffsiteBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OffsiteBookings_AspNetUsers_TransportEmployeeId",
                        column: x => x.TransportEmployeeId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OffsiteBookings_GroomingQuoteRevisions_EstimateId_EstimateVersion",
                        columns: x => new { x.EstimateId, x.EstimateVersion },
                        principalTable: "GroomingQuoteRevisions",
                        principalColumns: new[] { "QuoteId", "Number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OffsiteBookings_EstimateId_EstimateVersion",
                table: "OffsiteBookings",
                columns: new[] { "EstimateId", "EstimateVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_OffsiteBookings_EstimateId_Kind",
                table: "OffsiteBookings",
                columns: new[] { "EstimateId", "Kind" },
                unique: true,
                filter: "[Status] <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_OffsiteBookings_Status_RequestedAtUtc",
                table: "OffsiteBookings",
                columns: new[] { "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OffsiteBookings_TransportEmployeeId_TransportStartUtc",
                table: "OffsiteBookings",
                columns: new[] { "TransportEmployeeId", "TransportStartUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OffsiteBookings");
        }
    }
}
