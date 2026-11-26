using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddGroomingCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroomingServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    Species = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Breed = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Size = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroomingServices", x => x.Id);
                    table.CheckConstraint("CK_GroomingServices_Duration", "([DurationMinutes] IS NULL AND [IsActive] = 0) OR ([DurationMinutes] IS NOT NULL AND [DurationMinutes] > 0)");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroomingServices");
        }
    }
}
