using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class CompleteGroomingPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroomingPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GroomingQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LatestRevision = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingQuotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroomingQuotes_PetRecords_PetId",
                        column: x => x.PetId,
                        principalTable: "PetRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GroomingRecipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingRecipes", x => x.Id);
                    table.CheckConstraint("CK_GroomingRecipes_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_GroomingRecipes_GroomingServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "GroomingServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroomingRecipes_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GroomingPackageServices",
                columns: table => new
                {
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingPackageServices", x => new { x.PackageId, x.ServiceId });
                    table.ForeignKey(
                        name: "FK_GroomingPackageServices_GroomingPackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "GroomingPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroomingPackageServices_GroomingServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "GroomingServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GroomingPriceRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Species = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Breed = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Size = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingPriceRules", x => x.Id);
                    table.CheckConstraint("CK_GroomingPriceRules_Amount", "[Amount] >= 0");
                    table.CheckConstraint("CK_GroomingPriceRules_Target", "([ServiceId] IS NULL AND [PackageId] IS NOT NULL) OR ([ServiceId] IS NOT NULL AND [PackageId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_GroomingPriceRules_GroomingPackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "GroomingPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroomingPriceRules_GroomingServices_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "GroomingServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GroomingQuoteRevisions",
                columns: table => new
                {
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Snapshot = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingQuoteRevisions", x => new { x.QuoteId, x.Number });
                    table.ForeignKey(
                        name: "FK_GroomingQuoteRevisions_GroomingQuotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "GroomingQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroomingPackageServices_ServiceId",
                table: "GroomingPackageServices",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_GroomingPriceRules_PackageId",
                table: "GroomingPriceRules",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_GroomingPriceRules_ServiceId",
                table: "GroomingPriceRules",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_GroomingQuotes_PetId",
                table: "GroomingQuotes",
                column: "PetId");

            migrationBuilder.CreateIndex(
                name: "IX_GroomingRecipes_ProductId",
                table: "GroomingRecipes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_GroomingRecipes_ServiceId_ProductId",
                table: "GroomingRecipes",
                columns: new[] { "ServiceId", "ProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroomingPackageServices");

            migrationBuilder.DropTable(
                name: "GroomingPriceRules");

            migrationBuilder.DropTable(
                name: "GroomingQuoteRevisions");

            migrationBuilder.DropTable(
                name: "GroomingRecipes");

            migrationBuilder.DropTable(
                name: "GroomingPackages");

            migrationBuilder.DropTable(
                name: "GroomingQuotes");
        }
    }
}
