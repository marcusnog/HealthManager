using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PatientCareRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PatientCareRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivableId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    FieldsJson = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientCareRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientCareRecords_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientCareRecords_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientCareRecords_PatientApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "PatientApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientCareRecords_PatientCareRecords_ContractId",
                        column: x => x.ContractId,
                        principalTable: "PatientCareRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientCareRecords_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientCareRecords_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientCareRecords_Receivables_ReceivableId",
                        column: x => x.ReceivableId,
                        principalTable: "Receivables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientCareRecords_ApplicationId_Source",
                table: "PatientCareRecords",
                columns: new[] { "ApplicationId", "Source" },
                unique: true,
                filter: "\"Source\" = 'etapas'");

            migrationBuilder.CreateIndex(
                name: "IX_PatientCareRecords_AppointmentId_Source",
                table: "PatientCareRecords",
                columns: new[] { "AppointmentId", "Source" },
                unique: true,
                filter: "\"Source\" IN ('horarios', 'servicos')");

            migrationBuilder.CreateIndex(
                name: "IX_PatientCareRecords_ClinicId_PatientId_Source",
                table: "PatientCareRecords",
                columns: new[] { "ClinicId", "PatientId", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientCareRecords_ClinicId_Source_Date",
                table: "PatientCareRecords",
                columns: new[] { "ClinicId", "Source", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientCareRecords_ContractId",
                table: "PatientCareRecords",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientCareRecords_DoctorId",
                table: "PatientCareRecords",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientCareRecords_PatientId",
                table: "PatientCareRecords",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientCareRecords_PaymentId",
                table: "PatientCareRecords",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientCareRecords_ReceivableId",
                table: "PatientCareRecords",
                column: "ReceivableId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatientCareRecords");
        }
    }
}
