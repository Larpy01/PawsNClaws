using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddGroomingCashBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroomingBills",
                columns: table => new
                {
                    VisitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EstimateVersion = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EstimateSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingBills", x => x.VisitId);
                    table.CheckConstraint("CK_GroomingBills_Total", "[Total] >= 0");
                    table.ForeignKey(
                        name: "FK_GroomingBills_GroomingVisits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "GroomingVisits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GroomingCashPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VisitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Tendered = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RecordedByEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingCashPayments", x => x.Id);
                    table.CheckConstraint("CK_GroomingCashPayments_Amounts", "[Amount] > 0 AND [Tendered] >= [Amount]");
                    table.ForeignKey(
                        name: "FK_GroomingCashPayments_GroomingBills_VisitId",
                        column: x => x.VisitId,
                        principalTable: "GroomingBills",
                        principalColumn: "VisitId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroomingBills_IssuedAtUtc",
                table: "GroomingBills",
                column: "IssuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GroomingCashPayments_VisitId",
                table: "GroomingCashPayments",
                column: "VisitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroomingCashPayments");

            migrationBuilder.DropTable(
                name: "GroomingBills");
        }
    }
}
