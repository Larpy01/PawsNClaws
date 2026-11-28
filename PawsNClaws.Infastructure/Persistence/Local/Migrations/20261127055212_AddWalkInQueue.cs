using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddWalkInQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WalkInQueue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArrivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CalledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CalledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PriorityReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VisitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalkInQueue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalkInQueue_GroomingVisits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "GroomingVisits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalkInQueue_PetRecords_PetId",
                        column: x => x.PetId,
                        principalTable: "PetRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalkInQueue_IsOpen_Status_ArrivedAtUtc_Id",
                table: "WalkInQueue",
                columns: new[] { "IsOpen", "Status", "ArrivedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WalkInQueue_PetId",
                table: "WalkInQueue",
                column: "PetId",
                unique: true,
                filter: "[IsOpen] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_WalkInQueue_VisitId",
                table: "WalkInQueue",
                column: "VisitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalkInQueue");
        }
    }
}
