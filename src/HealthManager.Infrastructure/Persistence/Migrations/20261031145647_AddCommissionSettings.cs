using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommissionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommissionSettingsJson",
                table: "Clinics",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommissionSettingsJson",
                table: "Clinics");
        }
    }
}
