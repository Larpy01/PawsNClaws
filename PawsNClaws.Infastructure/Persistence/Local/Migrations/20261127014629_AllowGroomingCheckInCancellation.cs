using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AllowGroomingCheckInCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_GroomingCheckIns",
                table: "GroomingCheckIns");

            migrationBuilder.AddColumn<Guid>(
                name: "RevisionToken",
                table: "GroomingVisits",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "GroomingCheckIns",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "GroomingCheckIns",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "GroomingCheckIns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByEmployeeId",
                table: "GroomingCheckIns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledEstimateVersion",
                table: "GroomingCheckIns",
                type: "int",
                nullable: true);

            // The old key is already unique and provides a stable ID for each existing check-in.
            migrationBuilder.Sql("EXEC(N'UPDATE dbo.GroomingCheckIns SET Id = VisitId');");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GroomingCheckIns",
                table: "GroomingCheckIns",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_GroomingCheckIns_VisitId",
                table: "GroomingCheckIns",
                column: "VisitId",
                unique: true,
                filter: "[CancelledAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                EXEC(N'IF EXISTS (SELECT 1 FROM dbo.GroomingCheckIns WHERE CancelledAtUtc IS NOT NULL)
                    THROW 51000, ''Cannot downgrade while cancelled check-in history exists.'', 1;');
                """);

            migrationBuilder.DropPrimaryKey(
                name: "PK_GroomingCheckIns",
                table: "GroomingCheckIns");

            migrationBuilder.DropIndex(
                name: "IX_GroomingCheckIns_VisitId",
                table: "GroomingCheckIns");

            migrationBuilder.DropColumn(
                name: "RevisionToken",
                table: "GroomingVisits");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "GroomingCheckIns");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "GroomingCheckIns");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "GroomingCheckIns");

            migrationBuilder.DropColumn(
                name: "CancelledByEmployeeId",
                table: "GroomingCheckIns");

            migrationBuilder.DropColumn(
                name: "CancelledEstimateVersion",
                table: "GroomingCheckIns");

            migrationBuilder.AddPrimaryKey(
                name: "PK_GroomingCheckIns",
                table: "GroomingCheckIns",
                column: "VisitId");
        }
    }
}
