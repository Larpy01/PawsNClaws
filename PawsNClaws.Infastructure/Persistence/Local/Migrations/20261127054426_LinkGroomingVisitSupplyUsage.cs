using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class LinkGroomingVisitSupplyUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroomingVisitId",
                table: "InventoryMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_GroomingVisitId_ProductId",
                table: "InventoryMovements",
                columns: new[] { "GroomingVisitId", "ProductId" },
                unique: true,
                filter: "[GroomingVisitId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_GroomingVisits_GroomingVisitId",
                table: "InventoryMovements",
                column: "GroomingVisitId",
                principalTable: "GroomingVisits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_GroomingVisits_GroomingVisitId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_GroomingVisitId_ProductId",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "GroomingVisitId",
                table: "InventoryMovements");
        }
    }
}
