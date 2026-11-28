using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddCashDrawerReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashDrawerShifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpeningCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OpenedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CountedCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ExpectedAtClose = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashDrawerShifts", x => x.Id);
                    table.CheckConstraint("CK_CashDrawerShifts_Amounts", "[OpeningCash] >= 0 AND (([ClosedAtUtc] IS NULL AND [CountedCash] IS NULL AND [ExpectedAtClose] IS NULL AND [ClosedByEmployeeId] IS NULL) OR ([ClosedAtUtc] IS NOT NULL AND [CountedCash] IS NOT NULL AND [CountedCash] >= 0 AND [ExpectedAtClose] IS NOT NULL AND [ExpectedAtClose] >= 0 AND [ClosedByEmployeeId] IS NOT NULL))");
                });

            migrationBuilder.CreateTable(
                name: "CashDrawerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RecordedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashDrawerEntries", x => x.Id);
                    table.CheckConstraint("CK_CashDrawerEntries_Amount", "[Amount] <> 0");
                    table.ForeignKey(
                        name: "FK_CashDrawerEntries_CashDrawerShifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "CashDrawerShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashDrawerEntries_Kind_ReferenceId",
                table: "CashDrawerEntries",
                columns: new[] { "Kind", "ReferenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashDrawerEntries_ShiftId_RecordedAtUtc",
                table: "CashDrawerEntries",
                columns: new[] { "ShiftId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CashDrawerShifts_ClosedHistory",
                table: "CashDrawerShifts",
                column: "ClosedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CashDrawerShifts_OnlyOpenShift",
                table: "CashDrawerShifts",
                column: "ClosedAtUtc",
                unique: true,
                filter: "[ClosedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashDrawerEntries");

            migrationBuilder.DropTable(
                name: "CashDrawerShifts");
        }
    }
}
