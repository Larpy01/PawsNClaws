using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class RecordGroomingPickup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CollectedBy",
                table: "GroomingVisits",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PickedUpAtUtc",
                table: "GroomingVisits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PickupRecordedByEmployeeId",
                table: "GroomingVisits",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CollectedBy",
                table: "GroomingVisits");

            migrationBuilder.DropColumn(
                name: "PickedUpAtUtc",
                table: "GroomingVisits");

            migrationBuilder.DropColumn(
                name: "PickupRecordedByEmployeeId",
                table: "GroomingVisits");
        }
    }
}
