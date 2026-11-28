using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class RecordSupplierInvoiceVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SupplierInvoiceVat",
                table: "StockReceipts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockReceipts_SupplierInvoiceVat",
                table: "StockReceipts",
                sql: "[SupplierInvoiceVat] IS NULL OR ([SupplierInvoiceVat] >= 0 AND [Accepted] > 0 AND [Reference] IS NOT NULL AND LEN([Reference]) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockReceipts_SupplierInvoiceVat",
                table: "StockReceipts");

            migrationBuilder.DropColumn(
                name: "SupplierInvoiceVat",
                table: "StockReceipts");
        }
    }
}
