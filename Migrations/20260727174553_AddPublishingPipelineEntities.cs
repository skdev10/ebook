using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBookDashboard.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishingPipelineEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally scoped to new publishing tables only.
            // Unrelated snapshot drift (identity annotations, seed timestamps) is omitted
            // so applying this migration does not alter existing production tables.

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Subtitle = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AuthorName = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProjectType = table.Column<int>(type: "int", nullable: false),
                    TrimWidthIn = table.Column<double>(type: "double", precision: 10, scale: 4, nullable: false),
                    TrimHeightIn = table.Column<double>(type: "double", precision: 10, scale: 4, nullable: false),
                    IsCustomTrim = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PaperType = table.Column<int>(type: "int", nullable: false),
                    HasBleed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "cover_projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    CoverType = table.Column<int>(type: "int", nullable: false),
                    ComputedSpineWidthIn = table.Column<double>(type: "double", precision: 10, scale: 4, nullable: false),
                    ComputedTotalWidthIn = table.Column<double>(type: "double", precision: 10, scale: 4, nullable: false),
                    ComputedTotalHeightIn = table.Column<double>(type: "double", precision: 10, scale: 4, nullable: false),
                    PageCountUsedForSpine = table.Column<int>(type: "int", nullable: false),
                    DesignJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UploadedFilePath = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ThumbnailPath = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cover_projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cover_projects_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "export_jobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    Format = table.Column<int>(type: "int", nullable: false),
                    IncludeCover = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IncludeFrontMatter = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IncludeBackMatter = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OutputFilePath = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ErrorMessage = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_export_jobs_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "layout_profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    MarginTopIn = table.Column<double>(type: "double", precision: 10, scale: 4, nullable: false),
                    MarginBottomIn = table.Column<double>(type: "double", precision: 10, scale: 4, nullable: false),
                    MarginOutsideIn = table.Column<double>(type: "double", precision: 10, scale: 4, nullable: false),
                    MarginInsideIn = table.Column<double>(type: "double", precision: 10, scale: 4, nullable: false),
                    UseRecommendedMargins = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MarginsLocked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    BleedMode = table.Column<int>(type: "int", nullable: false),
                    BodyFontFamily = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BodyFontSizePt = table.Column<double>(type: "double", nullable: false),
                    LineSpacing = table.Column<double>(type: "double", nullable: false),
                    ParagraphSpacingBeforePt = table.Column<double>(type: "double", nullable: false),
                    ParagraphSpacingAfterPt = table.Column<double>(type: "double", nullable: false),
                    TextAlignment = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    H1FontFamily = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    H1FontSizePt = table.Column<double>(type: "double", nullable: false),
                    H2FontFamily = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    H2FontSizePt = table.Column<double>(type: "double", nullable: false),
                    TocFontSizePt = table.Column<double>(type: "double", nullable: false),
                    HeaderFooterFontSizePt = table.Column<double>(type: "double", nullable: false),
                    PageNumberPosition = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ShowPageNumbers = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ChaptersStartOnRecto = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_layout_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_layout_profiles_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "manuscript_versions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StoredFilePath = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceFormat = table.Column<int>(type: "int", nullable: false),
                    ParsedStructureJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UploadedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manuscript_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_manuscript_versions_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "book_sections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ManuscriptVersionId = table.Column<int>(type: "int", nullable: false),
                    ParentSectionId = table.Column<int>(type: "int", nullable: true),
                    MatterType = table.Column<int>(type: "int", nullable: false),
                    SectionKind = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    ContentHtml = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartsOnRecto = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsBlank = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StartPageNumber = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_book_sections_book_sections_ParentSectionId",
                        column: x => x.ParentSectionId,
                        principalTable: "book_sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_book_sections_manuscript_versions_ManuscriptVersionId",
                        column: x => x.ManuscriptVersionId,
                        principalTable: "manuscript_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_book_sections_ManuscriptVersionId_OrderIndex",
                table: "book_sections",
                columns: new[] { "ManuscriptVersionId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_book_sections_ParentSectionId",
                table: "book_sections",
                column: "ParentSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_cover_projects_ProjectId",
                table: "cover_projects",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_export_jobs_ProjectId_Status",
                table: "export_jobs",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_layout_profiles_ProjectId",
                table: "layout_profiles",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manuscript_versions_ProjectId_IsActive",
                table: "manuscript_versions",
                columns: new[] { "ProjectId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_manuscript_versions_ProjectId_VersionNumber",
                table: "manuscript_versions",
                columns: new[] { "ProjectId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_projects_UserId",
                table: "projects",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "book_sections");
            migrationBuilder.DropTable(name: "cover_projects");
            migrationBuilder.DropTable(name: "export_jobs");
            migrationBuilder.DropTable(name: "layout_profiles");
            migrationBuilder.DropTable(name: "manuscript_versions");
            migrationBuilder.DropTable(name: "projects");
        }
    }
}
