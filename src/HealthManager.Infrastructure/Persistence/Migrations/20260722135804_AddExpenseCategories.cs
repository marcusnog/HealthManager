using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HealthManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
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
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO "ExpenseCategories" ("Id", "ClinicId", "Name", "CreatedAt", "UpdatedAt")
                SELECT (substr(md5(e."ClinicId"::text || ':' || e."Category"::text),1,8)||'-'||substr(md5(e."ClinicId"::text || ':' || e."Category"::text),9,4)||'-'||substr(md5(e."ClinicId"::text || ':' || e."Category"::text),13,4)||'-'||substr(md5(e."ClinicId"::text || ':' || e."Category"::text),17,4)||'-'||substr(md5(e."ClinicId"::text || ':' || e."Category"::text),21,12))::uuid,
                       e."ClinicId",
                       CASE e."Category" WHEN 1 THEN 'Materiais' WHEN 2 THEN 'Equipamentos' WHEN 3 THEN 'Salarios' WHEN 4 THEN 'Marketing' WHEN 5 THEN 'Servicos publicos' WHEN 6 THEN 'Aluguel' ELSE 'Outros' END,
                       NOW(), NOW()
                FROM "Expenses" e
                GROUP BY e."ClinicId", e."Category";

                UPDATE "Expenses" e SET "CategoryId" = c."Id"
                FROM "ExpenseCategories" c
                WHERE c."ClinicId" = e."ClinicId"
                  AND c."Id" = (substr(md5(e."ClinicId"::text || ':' || e."Category"::text),1,8)||'-'||substr(md5(e."ClinicId"::text || ':' || e."Category"::text),9,4)||'-'||substr(md5(e."ClinicId"::text || ':' || e."Category"::text),13,4)||'-'||substr(md5(e."ClinicId"::text || ':' || e."Category"::text),17,4)||'-'||substr(md5(e."ClinicId"::text || ':' || e."Category"::text),21,12))::uuid;
                """);

            migrationBuilder.AlterColumn<Guid>(name: "CategoryId", table: "Expenses", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.DropColumn(name: "Category", table: "Expenses");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CategoryId",
                table: "Expenses",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_ClinicId_Name",
                table: "ExpenseCategories",
                columns: new[] { "ClinicId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_ExpenseCategories_CategoryId",
                table: "Expenses",
                column: "CategoryId",
                principalTable: "ExpenseCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "Category", table: "Expenses", type: "integer", nullable: false, defaultValue: 7);
            migrationBuilder.Sql("""
                UPDATE "Expenses" e SET "Category" = CASE c."Name"
                  WHEN 'Materiais' THEN 1 WHEN 'Equipamentos' THEN 2 WHEN 'Salarios' THEN 3
                  WHEN 'Marketing' THEN 4 WHEN 'Servicos publicos' THEN 5 WHEN 'Aluguel' THEN 6 ELSE 7 END
                FROM "ExpenseCategories" c WHERE c."Id" = e."CategoryId";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_ExpenseCategories_CategoryId",
                table: "Expenses");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_CategoryId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Expenses");

        }
    }
}
