using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManualPaymentPatient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PatientId",
                table: "Receivables",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Receivables_PatientId",
                table: "Receivables",
                column: "PatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Receivables_Patients_PatientId",
                table: "Receivables",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Receivables_Patients_PatientId",
                table: "Receivables");

            migrationBuilder.DropIndex(
                name: "IX_Receivables_PatientId",
                table: "Receivables");

            migrationBuilder.DropColumn(
                name: "PatientId",
                table: "Receivables");
        }
    }
}
