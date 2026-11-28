using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AuditTransportCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OffsiteBookings_EstimateId_Kind",
                table: "OffsiteBookings");

            migrationBuilder.AddColumn<string>(
                name: "TransportCancellationReason",
                table: "OffsiteBookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TransportCancelledAtUtc",
                table: "OffsiteBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TransportCancelledBy",
                table: "OffsiteBookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OffsiteBookings_EstimateId_Kind",
                table: "OffsiteBookings",
                columns: new[] { "EstimateId", "Kind" },
                unique: true,
                filter: "[Status] <> 3 AND [Status] <> 7");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OffsiteBookings_EstimateId_Kind",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "TransportCancellationReason",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "TransportCancelledAtUtc",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "TransportCancelledBy",
                table: "OffsiteBookings");

            migrationBuilder.CreateIndex(
                name: "IX_OffsiteBookings_EstimateId_Kind",
                table: "OffsiteBookings",
                columns: new[] { "EstimateId", "Kind" },
                unique: true,
                filter: "[Status] <> 3");
        }
    }
}
