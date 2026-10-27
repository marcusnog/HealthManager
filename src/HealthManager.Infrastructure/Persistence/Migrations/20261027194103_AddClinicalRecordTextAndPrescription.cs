using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalRecordTextAndPrescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Content",
                table: "ClinicalRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Prescription",
                table: "ClinicalRecords",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Content",
                table: "ClinicalRecords");

            migrationBuilder.DropColumn(
                name: "Prescription",
                table: "ClinicalRecords");
        }
    }
}
