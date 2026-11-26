using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddStockOutApprovalFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StockOutRequestId",
                table: "InventoryMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockOutRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ReasonCategory = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmissionOperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionComment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockOutRequests", x => x.Id);
                    table.UniqueConstraint("AK_StockOutRequests_Id_ProductId", x => new { x.Id, x.ProductId });
                    table.CheckConstraint("CK_StockOutRequests_Decision", "([Status] = 'Pending' AND [DecidedByUserId] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionComment] IS NULL)\nOR\n([Status] IN ('Approved', 'Rejected', 'Cancelled') AND [DecidedByUserId] IS NOT NULL\n    AND [DecidedAtUtc] IS NOT NULL AND [DecidedAtUtc] >= [SubmittedAtUtc]\n    AND ([Status] = 'Cancelled' OR [DecidedByUserId] <> [SubmittedByUserId])\n    AND ([Status] <> 'Rejected'\n        OR ([DecisionComment] IS NOT NULL AND LEN(LTRIM(RTRIM([DecisionComment]))) > 0)))");
                    table.CheckConstraint("CK_StockOutRequests_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_StockOutRequests_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_StockOutRequests_ReasonCategory", "[ReasonCategory] IN ('Defective', 'Expired', 'DamagedUponDelivery', 'TornPackaging', 'DamagedInStore', 'Other')");
                    table.ForeignKey(
                        name: "FK_StockOutRequests_AspNetUsers_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockOutRequests_AspNetUsers_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockOutRequests_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockOutRequestEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockOutRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_StockOutRequestEvidence", x => x.Id);
                    table.CheckConstraint("CK_StockOutRequestEvidence_ContentType", "[ContentType] IN ('image/jpeg', 'image/png')");
                    table.CheckConstraint("CK_StockOutRequestEvidence_FileSize", "[FileSizeBytes] > 0 AND [FileSizeBytes] <= 5242880");
                    table.CheckConstraint("CK_StockOutRequestEvidence_HashLength", "LEN([Sha256Hash]) = 64");
                    table.ForeignKey(
                        name: "FK_StockOutRequestEvidence_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockOutRequestEvidence_StockOutRequests_StockOutRequestId",
                        column: x => x.StockOutRequestId,
                        principalTable: "StockOutRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_InventoryMovements_StockOutRequestId",
                table: "InventoryMovements",
                columns: new[] { "StockOutRequestId", "ProductId" },
                unique: true,
                filter: "[StockOutRequestId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryMovements_StockOutRequest",
                table: "InventoryMovements",
                sql: "[StockOutRequestId] IS NULL OR [MovementType] = 'StockOut'");

            migrationBuilder.CreateIndex(
                name: "IX_StockOutRequestEvidence_StockOutRequestId",
                table: "StockOutRequestEvidence",
                column: "StockOutRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOutRequestEvidence_UploadedByUserId",
                table: "StockOutRequestEvidence",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_StockOutRequestEvidence_StorageKey",
                table: "StockOutRequestEvidence",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockOutRequests_DecidedByUserId",
                table: "StockOutRequests",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOutRequests_ProductId",
                table: "StockOutRequests",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockOutRequests_Status_SubmittedAtUtc",
                table: "StockOutRequests",
                columns: new[] { "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockOutRequests_SubmittedByUserId",
                table: "StockOutRequests",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_StockOutRequests_SubmissionOperationId",
                table: "StockOutRequests",
                column: "SubmissionOperationId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_StockOutRequests_StockOutRequestId_ProductId",
                table: "InventoryMovements",
                columns: new[] { "StockOutRequestId", "ProductId" },
                principalTable: "StockOutRequests",
                principalColumns: new[] { "Id", "ProductId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_StockOutRequests_StockOutRequestId_ProductId",
                table: "InventoryMovements");

            migrationBuilder.DropTable(
                name: "StockOutRequestEvidence");

            migrationBuilder.DropTable(
                name: "StockOutRequests");

            migrationBuilder.DropIndex(
                name: "UX_InventoryMovements_StockOutRequestId",
                table: "InventoryMovements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryMovements_StockOutRequest",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "StockOutRequestId",
                table: "InventoryMovements");
        }
    }
}
