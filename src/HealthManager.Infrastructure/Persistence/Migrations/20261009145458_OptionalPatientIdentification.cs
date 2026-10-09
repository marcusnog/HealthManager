using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptionalPatientIdentification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicId_Cpf",
                table: "Patients");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicId_Cpf",
                table: "Patients",
                columns: new[] { "ClinicId", "Cpf" },
                unique: true,
                filter: "\"Cpf\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Patients_ClinicId_Cpf",
                table: "Patients");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_ClinicId_Cpf",
                table: "Patients",
                columns: new[] { "ClinicId", "Cpf" },
                unique: true);
        }
    }
}
