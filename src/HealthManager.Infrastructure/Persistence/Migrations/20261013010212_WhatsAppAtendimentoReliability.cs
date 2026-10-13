using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppAtendimentoReliability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "WhatsAppMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProviderTimestamp",
                table: "WhatsAppMessages",
                type: "timestamp with time zone",
                nullable: true);

            // Normalize history without deleting messages; retain the earliest provider id on legacy duplicates.
            migrationBuilder.Sql("""
                UPDATE "WhatsAppMessages"
                SET "Phone" = CASE WHEN length(regexp_replace("Phone", '[^0-9]', '', 'g')) IN (10, 11)
                    THEN '55' || regexp_replace("Phone", '[^0-9]', '', 'g')
                    ELSE regexp_replace("Phone", '[^0-9]', '', 'g') END;
                UPDATE "WhatsAppMessages" SET "ProviderMessageId" = NULL WHERE "ProviderMessageId" = '';
                WITH duplicates AS (
                    SELECT "Id", row_number() OVER (
                        PARTITION BY "ClinicId", "Direction", "ProviderMessageId" ORDER BY "CreatedAt", "Id") AS rank
                    FROM "WhatsAppMessages" WHERE "ProviderMessageId" IS NOT NULL
                )
                UPDATE "WhatsAppMessages" SET "ProviderMessageId" = NULL
                FROM duplicates WHERE "WhatsAppMessages"."Id" = duplicates."Id" AND duplicates.rank > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ClinicId_ClientRequestId",
                table: "WhatsAppMessages",
                columns: new[] { "ClinicId", "ClientRequestId" },
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ClinicId_Direction_ProviderMessageId",
                table: "WhatsAppMessages",
                columns: new[] { "ClinicId", "Direction", "ProviderMessageId" },
                unique: true,
                filter: "\"ProviderMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessages_ClinicId_Phone_CreatedAt",
                table: "WhatsAppMessages",
                columns: new[] { "ClinicId", "Phone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicWhatsAppConfigs_PhoneNumberOfId",
                table: "ClinicWhatsAppConfigs",
                column: "PhoneNumberOfId",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_ClinicId_ClientRequestId",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_ClinicId_Direction_ProviderMessageId",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_WhatsAppMessages_ClinicId_Phone_CreatedAt",
                table: "WhatsAppMessages");

            migrationBuilder.DropIndex(
                name: "IX_ClinicWhatsAppConfigs_PhoneNumberOfId",
                table: "ClinicWhatsAppConfigs");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "WhatsAppMessages");

            migrationBuilder.DropColumn(
                name: "ProviderTimestamp",
                table: "WhatsAppMessages");
        }
    }
}
