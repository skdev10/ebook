using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBookDashboard.Migrations
{
    /// <summary>
    /// Baseline migration for an existing database: records EF migration history without applying DDL.
    /// Schema was created outside this migration chain (or pre-dates source-controlled migrations).
    /// </summary>
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — database already matches the current model snapshot.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Baseline: reverting would imply dropping the entire schema; not scripted here.
        }
    }
}
