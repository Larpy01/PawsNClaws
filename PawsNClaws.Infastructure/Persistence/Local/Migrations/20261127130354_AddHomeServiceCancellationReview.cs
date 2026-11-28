using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeServiceCancellationReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomeCancellationReason",
                table: "OffsiteBookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HomeCancelledAtUtc",
                table: "OffsiteBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HomeCancelledBy",
                table: "OffsiteBookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReviewNote",
                table: "OffsiteBookings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundReviewedAtUtc",
                table: "OffsiteBookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundReviewedBy",
                table: "OffsiteBookings",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomeCancellationReason",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "HomeCancelledAtUtc",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "HomeCancelledBy",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "RefundReviewNote",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "RefundReviewedAtUtc",
                table: "OffsiteBookings");

            migrationBuilder.DropColumn(
                name: "RefundReviewedBy",
                table: "OffsiteBookings");
        }
    }
}
