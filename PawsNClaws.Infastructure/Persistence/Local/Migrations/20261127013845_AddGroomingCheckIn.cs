using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddGroomingCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroomingCheckIns",
                columns: table => new
                {
                    VisitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Assessment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WaiverBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    WaiverContentType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    WaiverSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingCheckIns", x => x.VisitId);
                    table.CheckConstraint("CK_GroomingCheckIns_WaiverSize", "DATALENGTH([WaiverBytes]) BETWEEN 1 AND 5242880");
                    table.ForeignKey(
                        name: "FK_GroomingCheckIns_AspNetUsers_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GroomingCheckIns_GroomingVisits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "GroomingVisits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroomingCheckIns_EmployeeId",
                table: "GroomingCheckIns",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroomingCheckIns");
        }
    }
}
