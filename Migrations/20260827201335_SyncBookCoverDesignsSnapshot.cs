using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EBookDashboard.Migrations
{
    /// <summary>
    /// Snapshot-only: <c>book_cover_designs</c> already exists in the database.
    /// Brings the EF model snapshot in line without recreating or dropping the table.
    /// </summary>
    public partial class SyncBookCoverDesignsSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
