using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MssqlRealtime.Api.Persistence.Migrations
{
    /// <summary>
    /// The stored statement grows from 500 characters to 4000.
    /// <para>
    /// The body is empty on purpose, and that is the whole point of writing it down: SQLite has
    /// no width on a TEXT column, so <c>HasMaxLength</c> is validation in EF and nothing on
    /// disk. Widening it therefore costs no table rebuild and touches no existing row — the
    /// migration exists only to move the model snapshot, so the next migration is not generated
    /// against a stale model. Deleting it as "empty" would bring the drift back.
    /// </para>
    /// <para>
    /// Rows written before this keep their 500-character text ending in an ellipsis. Nothing can
    /// restore them: the rest of the statement was cut at capture and never reached the disk.
    /// </para>
    /// </summary>
    public partial class LongestQueryTextLength : Migration
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
