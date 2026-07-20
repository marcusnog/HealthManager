using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandPatientLegacyFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcquisitionSource",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChildrenCount",
                table: "Patients",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cns",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommercialPhone",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanionName",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Company",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Complement",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Education",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeFromMarketing",
                table: "Patients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FatherName",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HealthInsuranceNumber",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVip",
                table: "Patients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MaritalStatus",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MedicalRecordNumber",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotherName",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Neighborhood",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Profession",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiveDirectMail",
                table: "Patients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferredBy",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Religion",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rg",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryPhone",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sex",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SocialName",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpouseName",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZipCode",
                table: "Patients",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquisitionSource",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ChildrenCount",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Cns",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "CommercialPhone",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "CompanionName",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Company",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Complement",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Education",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ExcludeFromMarketing",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "FatherName",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "HealthInsuranceNumber",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "IsVip",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "MaritalStatus",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "MedicalRecordNumber",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "MotherName",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Neighborhood",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Profession",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ReceiveDirectMail",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ReferredBy",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Religion",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Rg",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SecondaryPhone",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Sex",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SocialName",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "SpouseName",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ZipCode",
                table: "Patients");
        }
    }
}
