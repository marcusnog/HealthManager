using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppointmentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentTypes", x => x.Id);
                });

            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentTypeId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO "AppointmentTypes" ("Id", "ClinicId", "Name", "CreatedAt", "UpdatedAt")
                SELECT (substr(md5(a."ClinicId"::text || ':' || a."Type"),1,8)||'-'||substr(md5(a."ClinicId"::text || ':' || a."Type"),9,4)||'-'||substr(md5(a."ClinicId"::text || ':' || a."Type"),13,4)||'-'||substr(md5(a."ClinicId"::text || ':' || a."Type"),17,4)||'-'||substr(md5(a."ClinicId"::text || ':' || a."Type"),21,12))::uuid,
                       a."ClinicId", a."Type", NOW(), NOW()
                FROM "Appointments" a
                GROUP BY a."ClinicId", a."Type";

                UPDATE "Appointments" a SET "AppointmentTypeId" = t."Id"
                FROM "AppointmentTypes" t
                WHERE t."ClinicId" = a."ClinicId" AND t."Name" = a."Type";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "AppointmentTypeId",
                table: "Appointments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(name: "Type", table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_AppointmentTypeId",
                table: "Appointments",
                column: "AppointmentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTypes_ClinicId_Name",
                table: "AppointmentTypes",
                columns: new[] { "ClinicId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_AppointmentTypes_AppointmentTypeId",
                table: "Appointments",
                column: "AppointmentTypeId",
                principalTable: "AppointmentTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_AppointmentTypes_AppointmentTypeId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_AppointmentTypeId",
                table: "Appointments");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Appointments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "Appointments" a SET "Type" = t."Name"
                FROM "AppointmentTypes" t WHERE t."Id" = a."AppointmentTypeId";
                """);

            migrationBuilder.DropColumn(name: "AppointmentTypeId", table: "Appointments");
            migrationBuilder.DropTable(name: "AppointmentTypes");
        }
    }
}
