using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class CompleteInventoryWorkflow : Migration
    {
        private static readonly (string Table, string Name, string Expression)[] QuantityChecks =
        [
            ("StockOutRequests", "CK_StockOutRequests_Quantity", "[Quantity] > 0"),
            ("InventoryStocks", "CK_InventoryStocks_QuantityOnHand", "[QuantityOnHand] >= 0"),
            ("InventoryStocks", "CK_InventoryStocks_ReorderLevel", "[ReorderLevel] >= 0"),
            ("InventoryMovements", "CK_InventoryMovements_QuantityAfter", "[QuantityAfter] >= 0"),
            ("InventoryMovements", "CK_InventoryMovements_QuantityBefore", "[QuantityBefore] >= 0"),
            ("InventoryMovements", "CK_InventoryMovements_QuantityChange", "[QuantityChange] <> 0")
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT request.[ProductId]
                    FROM [dbo].[StockOutRequests] request
                    LEFT JOIN [dbo].[InventoryStocks] stock ON stock.[ProductId] = request.[ProductId]
                    WHERE request.[Status] = N'Pending'
                    GROUP BY request.[ProductId], stock.[QuantityOnHand]
                    HAVING SUM(CONVERT(decimal(18,3), request.[Quantity])) > COALESCE(stock.[QuantityOnHand], 0)
                )
                    THROW 51030, 'Pending Stock Out exceeds legacy stock. Reconcile before upgrading.', 1;
                """);

            foreach (var check in QuantityChecks)
                migrationBuilder.DropCheckConstraint(name: check.Name, table: check.Table);

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryMovements_Direction",
                table: "InventoryMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryMovements_MovementType",
                table: "InventoryMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryMovements_QuantityBalance",
                table: "InventoryMovements");

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "StockOutRequests",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "StockOutRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "Products",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Products",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Products",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsConsumable",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSellable",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVisibleOnline",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TracksExpiry",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "Products",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Each");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ReorderLevel",
                table: "InventoryStocks",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityOnHand",
                table: "InventoryStocks",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityChange",
                table: "InventoryMovements",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityBefore",
                table: "InventoryMovements",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "QuantityAfter",
                table: "InventoryMovements",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<Guid>(
                name: "ReversesMovementId",
                table: "InventoryMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UsageEstimateId",
                table: "InventoryMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GroomingUsageEstimates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingUsageEstimates", x => x.Id);
                    table.CheckConstraint("CK_GroomingUsageEstimates_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_GroomingUsageEstimates_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroomingUsageEstimates_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Lot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OnHand = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Quarantined = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryBatches", x => x.Id);
                    table.UniqueConstraint("AK_InventoryBatches_Id_ProductId", x => new { x.Id, x.ProductId });
                    table.CheckConstraint("CK_InventoryBatches_Balance", "[Quarantined] >= 0 AND [OnHand] >= [Quarantined]");
                    table.ForeignKey(
                        name: "FK_InventoryBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventorySuppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventorySuppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryMovementAllocations",
                columns: table => new
                {
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityChange = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovementAllocations", x => new { x.MovementId, x.BatchId });
                    table.CheckConstraint("CK_InventoryMovementAllocations_Quantity", "[QuantityChange] <> 0");
                    table.ForeignKey(
                        name: "FK_InventoryMovementAllocations_InventoryBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "InventoryBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryMovementAllocations_InventoryMovements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "InventoryMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryQuarantineEvents",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityChange = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryQuarantineEvents", x => x.OperationId);
                    table.CheckConstraint("CK_InventoryQuarantineEvents_Quantity", "[QuantityChange] <> 0");
                    table.ForeignKey(
                        name: "FK_InventoryQuarantineEvents_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryQuarantineEvents_InventoryBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "InventoryBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Supplier = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Lot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Delivered = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Accepted = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Rejected = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReceipts", x => x.Id);
                    table.CheckConstraint("CK_StockReceipts_Quantities", "[Delivered] > 0 AND [Accepted] >= 0 AND [Rejected] >= 0 AND [Delivered] = [Accepted] + [Rejected]");
                    table.ForeignKey(
                        name: "FK_StockReceipts_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReceipts_InventoryBatches_BatchId_ProductId",
                        columns: x => new { x.BatchId, x.ProductId },
                        principalTable: "InventoryBatches",
                        principalColumns: new[] { "Id", "ProductId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReceipts_InventorySuppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "InventorySuppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReceipts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockReceiptEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReceiptEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockReceiptEvidence_StockReceipts_StockReceiptId",
                        column: x => x.StockReceiptId,
                        principalTable: "StockReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockOutRequests_BatchId_ProductId",
                table: "StockOutRequests",
                columns: new[] { "BatchId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Barcode",
                table: "Products",
                column: "Barcode",
                unique: true,
                filter: "[Barcode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_ReversesMovementId",
                table: "InventoryMovements",
                column: "ReversesMovementId",
                unique: true,
                filter: "[ReversesMovementId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_UsageEstimateId",
                table: "InventoryMovements",
                column: "UsageEstimateId",
                unique: true,
                filter: "[UsageEstimateId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_Direction",
                table: "InventoryMovements",
                sql: "([MovementType] IN ('StockIn', 'SaleReversal', 'GroomingUsageCorrection') AND [QuantityChange] > 0) OR ([MovementType] IN ('StockOut', 'Sale', 'GroomingUsage') AND [QuantityChange] < 0) OR ([MovementType] = 'Adjustment')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_MovementType",
                table: "InventoryMovements",
                sql: "[MovementType] IN ('StockIn', 'StockOut', 'Adjustment', 'Sale', 'SaleReversal', 'GroomingUsage', 'GroomingUsageCorrection')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_QuantityBalance",
                table: "InventoryMovements",
                sql: "[QuantityAfter] = [QuantityBefore] + [QuantityChange]");

            foreach (var check in QuantityChecks)
                migrationBuilder.AddCheckConstraint(name: check.Name, table: check.Table, sql: check.Expression);

            // Establish opening batch balances without fabricating historical receipt or lot data.
            migrationBuilder.Sql(
                """
                INSERT INTO [dbo].[InventoryBatches] ([Id], [ProductId], [OnHand], [Quarantined])
                SELECT stock.[Id], stock.[ProductId], stock.[QuantityOnHand], COALESCE(holds.[Quantity], 0)
                FROM [dbo].[InventoryStocks] stock
                OUTER APPLY (
                    SELECT SUM(request.[Quantity]) AS [Quantity]
                    FROM [dbo].[StockOutRequests] request
                    WHERE request.[ProductId] = stock.[ProductId] AND request.[Status] = N'Pending'
                ) holds
                WHERE stock.[QuantityOnHand] > 0;

                UPDATE request
                SET [BatchId] = batch.[Id]
                FROM [dbo].[StockOutRequests] request
                INNER JOIN [dbo].[InventoryBatches] batch ON batch.[ProductId] = request.[ProductId]
                WHERE request.[Status] = N'Pending';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_GroomingUsageEstimates_ActorId",
                table: "GroomingUsageEstimates",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_GroomingUsageEstimates_ProductId_JobReference",
                table: "GroomingUsageEstimates",
                columns: new[] { "ProductId", "JobReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBatches_ProductId_ExpiryDate_ReceivedAtUtc",
                table: "InventoryBatches",
                columns: new[] { "ProductId", "ExpiryDate", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovementAllocations_BatchId",
                table: "InventoryMovementAllocations",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryQuarantineEvents_ActorId",
                table: "InventoryQuarantineEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryQuarantineEvents_BatchId",
                table: "InventoryQuarantineEvents",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_InventorySuppliers_NormalizedName",
                table: "InventorySuppliers",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReceiptEvidence_StockReceiptId",
                table: "StockReceiptEvidence",
                column: "StockReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReceiptEvidence_StorageKey",
                table: "StockReceiptEvidence",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReceipts_ActorId",
                table: "StockReceipts",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReceipts_BatchId_ProductId",
                table: "StockReceipts",
                columns: new[] { "BatchId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReceipts_ProductId",
                table: "StockReceipts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockReceipts_SupplierId",
                table: "StockReceipts",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_GroomingUsageEstimates_UsageEstimateId",
                table: "InventoryMovements",
                column: "UsageEstimateId",
                principalTable: "GroomingUsageEstimates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_InventoryMovements_ReversesMovementId",
                table: "InventoryMovements",
                column: "ReversesMovementId",
                principalTable: "InventoryMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockOutRequests_InventoryBatches_BatchId_ProductId",
                table: "StockOutRequests",
                columns: new[] { "BatchId", "ProductId" },
                principalTable: "InventoryBatches",
                principalColumns: new[] { "Id", "ProductId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Restore a verified backup to revert inventory workflow data and fractional quantities.");
    }
}
