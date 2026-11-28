using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddRetailOnlinePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OnlinePaymentReference",
                table: "RetailSales",
                type: "varchar(80)",
                unicode: false,
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "RetailSales",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_RetailSales_OnlinePaymentReference",
                table: "RetailSales",
                column: "OnlinePaymentReference",
                unique: true,
                filter: "[OnlinePaymentReference] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RetailSales_Payment",
                table: "RetailSales",
                sql: "([PaymentMethod] = 0 AND [OnlinePaymentReference] IS NULL) OR ([PaymentMethod] = 1 AND [Total] > 0 AND [Tendered] = [Total] AND [OnlinePaymentReference] IS NOT NULL AND LEN([OnlinePaymentReference]) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RetailSales_OnlinePaymentReference",
                table: "RetailSales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RetailSales_Payment",
                table: "RetailSales");

            migrationBuilder.DropColumn(
                name: "OnlinePaymentReference",
                table: "RetailSales");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "RetailSales");
        }
    }
}
