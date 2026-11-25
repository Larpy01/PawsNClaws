using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StockOutReason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    QuantityChange = table.Column<int>(type: "int", nullable: false),
                    QuantityBefore = table.Column<int>(type: "int", nullable: false),
                    QuantityAfter = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovements", x => x.Id);
                    table.CheckConstraint("CK_InventoryMovements_Direction", "([MovementType] IN ('StockIn', 'SaleReversal') AND [QuantityChange] > 0) OR ([MovementType] IN ('StockOut', 'Sale') AND [QuantityChange] < 0) OR ([MovementType] = 'Adjustment')");
                    table.CheckConstraint("CK_InventoryMovements_MovementType", "[MovementType] IN ('StockIn', 'StockOut', 'Adjustment', 'Sale', 'SaleReversal')");
                    table.CheckConstraint("CK_InventoryMovements_QuantityAfter", "[QuantityAfter] >= 0");
                    table.CheckConstraint("CK_InventoryMovements_QuantityBalance", "CAST([QuantityAfter] AS bigint) = CAST([QuantityBefore] AS bigint) + [QuantityChange]");
                    table.CheckConstraint("CK_InventoryMovements_QuantityBefore", "[QuantityBefore] >= 0");
                    table.CheckConstraint("CK_InventoryMovements_QuantityChange", "[QuantityChange] <> 0");
                    table.CheckConstraint("CK_InventoryMovements_StockOutReason", "(\r\n    [MovementType] = 'StockOut'\r\n    AND [StockOutReason] IS NOT NULL\r\n    AND [StockOutReason] IN\r\n    (\r\n        'Defective',\r\n        'Expired',\r\n        'DamagedUponDelivery',\r\n        'TornPackaging',\r\n        'DamagedInStore',\r\n        'Other'\r\n    )\r\n    AND [Reason] IS NOT NULL\r\n    AND LEN(LTRIM(RTRIM([Reason]))) > 0\r\n)\r\nOR\r\n(\r\n    [MovementType] <> 'StockOut'\r\n    AND [StockOutReason] IS NULL\r\n)");
                    table.ForeignKey(
                        name: "FK_InventoryMovements_AspNetUsers_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryStocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityOnHand = table.Column<int>(type: "int", nullable: false),
                    ReorderLevel = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryStocks", x => x.Id);
                    table.CheckConstraint("CK_InventoryStocks_QuantityOnHand", "[QuantityOnHand] >= 0");
                    table.CheckConstraint("CK_InventoryStocks_ReorderLevel", "[ReorderLevel] >= 0");
                    table.ForeignKey(
                        name: "FK_InventoryStocks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryMovementEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InventoryMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovementEvidence", x => x.Id);
                    table.CheckConstraint("CK_InventoryMovementEvidence_ContentType", "[ContentType] IN ('image/jpeg', 'image/png')");
                    table.CheckConstraint("CK_InventoryMovementEvidence_FileSize", "[FileSizeBytes] > 0 AND [FileSizeBytes] <= 5242880");
                    table.CheckConstraint("CK_InventoryMovementEvidence_HashLength", "LEN([Sha256Hash]) = 64");
                    table.ForeignKey(
                        name: "FK_InventoryMovementEvidence_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovementEvidence_InventoryMovements_InventoryMovementId",
                        column: x => x.InventoryMovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovementEvidence_InventoryMovementId",
                table: "InventoryMovementEvidence",
                column: "InventoryMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovementEvidence_UploadedByUserId",
                table: "InventoryMovementEvidence",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryMovementEvidence_StorageKey",
                table: "InventoryMovementEvidence",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_PerformedByUserId",
                table: "InventoryMovements",
                column: "PerformedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ProductId_CreatedAtUtc",
                table: "InventoryMovements",
                columns: new[] { "ProductId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryMovements_OperationId",
                table: "InventoryMovements",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_InventoryStocks_ProductId",
                table: "InventoryStocks",
                column: "ProductId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryMovementEvidence");

            migrationBuilder.DropTable(
                name: "InventoryStocks");

            migrationBuilder.DropTable(
                name: "InventoryMovements");
        }
    }
}
