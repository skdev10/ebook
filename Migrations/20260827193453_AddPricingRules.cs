using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EBookDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PricingRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Unit = table.Column<int>(type: "int", nullable: false),
                    UnitAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FreeAllowance = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingRules", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "PricingRules",
                columns: new[] { "Id", "Currency", "Description", "DisplayName", "FreeAllowance", "IsActive", "Key", "Unit", "UnitAmount", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { 1, "USD", null, "AI writing", 20, true, "writing.per_page", 0, 0.50m, new DateTime(2026, 8, 27, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 2, "USD", null, "Custom image cover", 0, true, "cover.custom_image", 1, 10.00m, new DateTime(2026, 8, 27, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 3, "USD", null, "Premium formatting", 0, true, "formatting.premium", 1, 3.00m, new DateTime(2026, 8, 27, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 4, "USD", null, "Paperback package", 0, true, "export.paperback", 2, 20.00m, new DateTime(2026, 8, 27, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { 5, "USD", null, "Hardcover package", 0, true, "export.hardcover", 2, 30.00m, new DateTime(2026, 8, 27, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PricingRules_Key",
                table: "PricingRules",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PricingRules");
        }
    }
}
