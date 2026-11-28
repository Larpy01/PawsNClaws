using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeServiceHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAtUtc",
                table: "OffsiteBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedBy",
                table: "OffsiteBookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepositReference",
                table: "OffsiteBookings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositRequired",
                table: "OffsiteBookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HoldExpiresAtUtc",
                table: "OffsiteBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HomeEndsAtUtc",
                table: "OffsiteBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HomeGroomerId",
                table: "OffsiteBookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VerifiedDeposit",
                table: "OffsiteBookings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OffsiteBookings_HomeGroomerId_RequestedAtUtc",
                table: "OffsiteBookings",
                columns: new[] { "HomeGroomerId", "RequestedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_OffsiteBookings_AspNetUsers_HomeGroomerId",
                table: "OffsiteBookings",
                column: "HomeGroomerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OffsiteBookings_AspNetUsers_HomeGroomerId",
                table: "OffsiteBookings");

            migrationBuilder.DropIndex(
                name: "IX_OffsiteBookings_HomeGroomerId_RequestedAtUtc",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "ConfirmedAtUtc",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "ConfirmedBy",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "DepositReference",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "DepositRequired",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "HoldExpiresAtUtc",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "HomeEndsAtUtc",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "HomeGroomerId",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "VerifiedDeposit",
                table: "OffsiteBookings");
        }
    }
}
