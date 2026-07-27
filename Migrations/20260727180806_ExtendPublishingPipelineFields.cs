using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBookDashboard.Migrations
{
    /// <inheritdoc />
    public partial class ExtendPublishingPipelineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PageCount",
                table: "projects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "FirstLineIndentIn",
                table: "layout_profiles",
                type: "double",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "IsEbookProfile",
                table: "layout_profiles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NoIndentOnFirstPara",
                table: "layout_profiles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LayoutTemplate",
                table: "book_sections",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_layout_profiles_ProjectId_IsEbookProfile",
                table: "layout_profiles",
                columns: new[] { "ProjectId", "IsEbookProfile" },
                unique: true);

            // The old unique index on ProjectId alone must be dropped only after the new
            // composite index exists, since MySQL requires an index on the leftmost FK
            // column to remain available at all times (the new index satisfies that).
            migrationBuilder.DropIndex(
                name: "IX_layout_profiles_ProjectId",
                table: "layout_profiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_layout_profiles_ProjectId",
                table: "layout_profiles",
                column: "ProjectId",
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_layout_profiles_ProjectId_IsEbookProfile",
                table: "layout_profiles");

            migrationBuilder.DropColumn(
                name: "PageCount",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "FirstLineIndentIn",
                table: "layout_profiles");

            migrationBuilder.DropColumn(
                name: "IsEbookProfile",
                table: "layout_profiles");

            migrationBuilder.DropColumn(
                name: "NoIndentOnFirstPara",
                table: "layout_profiles");

            migrationBuilder.DropColumn(
                name: "LayoutTemplate",
                table: "book_sections");
        }
    }
}
