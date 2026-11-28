using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddRetailSaleRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReturnsQuarantined",
                table: "RetailSales",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "RetailSales",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "RetailSales",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByEmployeeId",
                table: "RetailSales",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetailSales_RecordedAtUtc_Id",
                table: "RetailSales",
                columns: new[] { "RecordedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RetailSales_RecordedAtUtc_Id",
                table: "RetailSales");

            migrationBuilder.DropColumn(
                name: "ReturnsQuarantined",
                table: "RetailSales");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "RetailSales");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "RetailSales");

            migrationBuilder.DropColumn(
                name: "VoidedByEmployeeId",
                table: "RetailSales");
        }
    }
}
