using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class RecordRetailSaleVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "RetailSales",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "RetailSales",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RetailSales_Vat",
                table: "RetailSales",
                sql: "([VatRate] IS NULL AND [VatAmount] IS NULL) OR ([VatRate] IS NOT NULL AND [VatAmount] IS NOT NULL AND (([VatRate] = 0 AND [VatAmount] = 0) OR ([VatRate] > 0 AND [VatRate] <= 1 AND [VatAmount] = ROUND([Total] * [VatRate] / (1 + [VatRate]), 2))))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RetailSales_Vat",
                table: "RetailSales");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "RetailSales");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "RetailSales");
        }
    }
}
