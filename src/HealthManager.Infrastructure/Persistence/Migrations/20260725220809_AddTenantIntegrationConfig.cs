using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIntegrationConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultAppointmentMinutes",
                table: "Clinics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ClinicBrandings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    PrimaryColor = table.Column<string>(type: "text", nullable: true),
                    SecondaryColor = table.Column<string>(type: "text", nullable: true),
                    CustomClinicName = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicBrandings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicBrandings_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicNotificationConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReminderHoursBefore = table.Column<int>(type: "integer", nullable: false),
                    ChannelsJson = table.Column<string>(type: "text", nullable: false),
                    QuietHoursStart = table.Column<string>(type: "text", nullable: true),
                    QuietHoursEnd = table.Column<string>(type: "text", nullable: true),
                    EnableAutoReminders = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicNotificationConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicNotificationConfigs_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicPaymentGatewayConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ApiKey = table.Column<string>(type: "text", nullable: true),
                    Secret = table.Column<string>(type: "text", nullable: true),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    WebhookSecret = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicPaymentGatewayConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicPaymentGatewayConfigs_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicWhatsAppConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumberOfId = table.Column<string>(type: "text", nullable: false),
                    AccessToken = table.Column<string>(type: "text", nullable: false),
                    WABAId = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicWhatsAppConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicWhatsAppConfigs_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicBrandings_ClinicId",
                table: "ClinicBrandings",
                column: "ClinicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicNotificationConfigs_ClinicId",
                table: "ClinicNotificationConfigs",
                column: "ClinicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicPaymentGatewayConfigs_ClinicId",
                table: "ClinicPaymentGatewayConfigs",
                column: "ClinicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicWhatsAppConfigs_ClinicId",
                table: "ClinicWhatsAppConfigs",
                column: "ClinicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicBrandings");

            migrationBuilder.DropTable(
                name: "ClinicNotificationConfigs");

            migrationBuilder.DropTable(
                name: "ClinicPaymentGatewayConfigs");

            migrationBuilder.DropTable(
                name: "ClinicWhatsAppConfigs");

            migrationBuilder.DropColumn(
                name: "DefaultAppointmentMinutes",
                table: "Clinics");
        }
    }
}
