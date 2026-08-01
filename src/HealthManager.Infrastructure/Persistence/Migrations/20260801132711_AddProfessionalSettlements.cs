using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfessionalSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ClinicSharePercentage",
                table: "Receivables",
                type: "numeric",
                nullable: false,
                defaultValue: 100m);

            migrationBuilder.AddColumn<Guid>(
                name: "ProfessionalId",
                table: "Receivables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ClinicRevenueAmount",
                table: "Payments",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "FundsRecipient",
                table: "Payments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OwnerSettledAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProfessionalPaidAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfessionalPayableAmount",
                table: "Payments",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("UPDATE \"Payments\" SET \"ClinicRevenueAmount\" = \"Amount\"");

            migrationBuilder.AddColumn<decimal>(
                name: "ClinicSharePercentage",
                table: "Doctors",
                type: "numeric",
                nullable: false,
                defaultValue: 100m);

            migrationBuilder.CreateIndex(
                name: "IX_Receivables_ProfessionalId",
                table: "Receivables",
                column: "ProfessionalId");

            migrationBuilder.AddForeignKey(
                name: "FK_Receivables_Doctors_ProfessionalId",
                table: "Receivables",
                column: "ProfessionalId",
                principalTable: "Doctors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Receivables_Doctors_ProfessionalId",
                table: "Receivables");

            migrationBuilder.DropIndex(
                name: "IX_Receivables_ProfessionalId",
                table: "Receivables");

            migrationBuilder.DropColumn(
                name: "ClinicSharePercentage",
                table: "Receivables");

            migrationBuilder.DropColumn(
                name: "ProfessionalId",
                table: "Receivables");

            migrationBuilder.DropColumn(
                name: "ClinicRevenueAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "FundsRecipient",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "OwnerSettledAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProfessionalPaidAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProfessionalPayableAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ClinicSharePercentage",
                table: "Doctors");
        }
    }
}
