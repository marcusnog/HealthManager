using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClinicalWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicalCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Folder = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    FieldsJson = table.Column<string>(type: "text", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalCatalogItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalCatalogItems_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectsEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    FieldsJson = table.Column<string>(type: "text", nullable: false),
                    ValuesJson = table.Column<string>(type: "text", nullable: false),
                    SignatureMode = table.Column<string>(type: "text", nullable: false),
                    PatientName = table.Column<string>(type: "text", nullable: false),
                    DoctorName = table.Column<string>(type: "text", nullable: false),
                    DoctorCrm = table.Column<string>(type: "text", nullable: false),
                    ClinicName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalEntries_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalEntries_ClinicalEntries_CorrectsEntryId",
                        column: x => x.CorrectsEntryId,
                        principalTable: "ClinicalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalEntries_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalEntries_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalEntries_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalCatalogItems_ClinicId_Kind_OwnerUserId",
                table: "ClinicalCatalogItems",
                columns: new[] { "ClinicId", "Kind", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalCatalogItems_OwnerUserId",
                table: "ClinicalCatalogItems",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEntries_AppointmentId",
                table: "ClinicalEntries",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEntries_AuthorId",
                table: "ClinicalEntries",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEntries_ClinicId_AppointmentId_ClientRequestId",
                table: "ClinicalEntries",
                columns: new[] { "ClinicId", "AppointmentId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEntries_ClinicId_PatientId_CreatedAt",
                table: "ClinicalEntries",
                columns: new[] { "ClinicId", "PatientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEntries_CorrectsEntryId",
                table: "ClinicalEntries",
                column: "CorrectsEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEntries_DoctorId",
                table: "ClinicalEntries",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEntries_PatientId",
                table: "ClinicalEntries",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicalCatalogItems");

            migrationBuilder.DropTable(
                name: "ClinicalEntries");
        }
    }
}
