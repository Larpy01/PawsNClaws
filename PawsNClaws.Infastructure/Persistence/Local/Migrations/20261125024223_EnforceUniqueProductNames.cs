using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueProductNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Products_Name",
                table: "Products",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Products_Name",
                table: "Products");
        }
    }
}
