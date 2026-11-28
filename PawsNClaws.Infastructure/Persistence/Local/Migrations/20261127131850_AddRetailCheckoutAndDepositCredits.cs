using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddRetailCheckoutAndDepositCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "HomeDepositAmount",
                table: "GroomingBills",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "HomeDepositBookingId",
                table: "GroomingBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeDepositReference",
                table: "GroomingBills",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RetailSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Tendered = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetailSales", x => x.Id);
                    table.CheckConstraint("CK_RetailSales_Amounts", "[Total] >= 0 AND [Tendered] >= [Total]");
                });

            migrationBuilder.CreateTable(
                name: "RetailSaleLines",
                columns: table => new
                {
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetailSaleLines", x => new { x.SaleId, x.ProductId });
                    table.CheckConstraint("CK_RetailSaleLines_Amounts", "[Quantity] > 0 AND [UnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_RetailSaleLines_InventoryMovements_InventoryMovementId",
                        column: x => x.InventoryMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RetailSaleLines_RetailSales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "RetailSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroomingBills_HomeDepositBookingId",
                table: "GroomingBills",
                column: "HomeDepositBookingId",
                unique: true,
                filter: "[HomeDepositBookingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RetailSaleLines_InventoryMovementId",
                table: "RetailSaleLines",
                column: "InventoryMovementId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GroomingBills_OffsiteBookings_HomeDepositBookingId",
                table: "GroomingBills",
                column: "HomeDepositBookingId",
                principalTable: "OffsiteBookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroomingBills_OffsiteBookings_HomeDepositBookingId",
                table: "GroomingBills");

            migrationBuilder.DropTable(
                name: "RetailSaleLines");

            migrationBuilder.DropTable(
                name: "RetailSales");

            migrationBuilder.DropIndex(
                name: "IX_GroomingBills_HomeDepositBookingId",
                table: "GroomingBills");

            migrationBuilder.DropColumn(
                name: "HomeDepositAmount",
                table: "GroomingBills");

            migrationBuilder.DropColumn(
                name: "HomeDepositBookingId",
                table: "GroomingBills");

            migrationBuilder.DropColumn(
                name: "HomeDepositReference",
                table: "GroomingBills");
        }
    }
}
