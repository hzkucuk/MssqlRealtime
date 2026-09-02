using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MssqlRealtime.Api.Persistence.Migrations
{
    /// <summary>
    /// Who ran the longest query of a bucket, and what it was.
    /// <para>
    /// Additive and nullable on purpose: rows written before this version keep a number with
    /// no owner, which is the truth about them — the identity was never captured and inventing
    /// one would be worse than an empty cell. The reports screen shows the detail only where
    /// there is one.
    /// </para>
    /// </summary>
    public partial class LongestQueryOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LongestQueryBy",
                table: "MetricSamples",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LongestQueryText",
                table: "MetricSamples",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LongestQueryBy",
                table: "MetricSamples");

            migrationBuilder.DropColumn(
                name: "LongestQueryText",
                table: "MetricSamples");
        }
    }
}
