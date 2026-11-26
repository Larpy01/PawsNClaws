using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PawsNClaws.Infastructure.Persistence.Local.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalCustomerPetRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RecordConsent = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PetRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Species = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Breed = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Sex = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Size = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    WeightKg = table.Column<decimal>(type: "decimal(8,3)", precision: 8, scale: 3, nullable: true),
                    VaccinationStatus = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Allergies = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MedicalNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BehaviorNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SpecialInstructions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PetRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PetRecords_CustomerRecords_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "CustomerRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProfileChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SyncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileChanges_CustomerRecords_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "CustomerRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProfileChanges_PetRecords_PetId",
                        column: x => x.PetId,
                        principalTable: "PetRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRecords_LastName_FirstName",
                table: "CustomerRecords",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_PetRecords_CustomerId_Name",
                table: "PetRecords",
                columns: new[] { "CustomerId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileChanges_CustomerId",
                table: "ProfileChanges",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileChanges_PetId",
                table: "ProfileChanges",
                column: "PetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileChanges_RecordVersion",
                table: "ProfileChanges",
                column: "RecordVersion",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileChanges");

            migrationBuilder.DropTable(
                name: "PetRecords");

            migrationBuilder.DropTable(
                name: "CustomerRecords");
        }
    }
}
