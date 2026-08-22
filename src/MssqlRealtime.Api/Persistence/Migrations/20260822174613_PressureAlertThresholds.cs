using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MssqlRealtime.Api.Persistence.Migrations
{
    /// <summary>
    /// Three pressure thresholds: longest blocking wait, CPU queue depth, worker pool usage.
    /// <para>
    /// A new column arrives NULL on rows that already exist, and NULL means "rule off" — so
    /// without the backfill below every server registered before this version would run
    /// without the new rules while the form showed them as available. That is the same trap
    /// the session-count default hit one version earlier (docs/04-kirilma-noktalari.md).
    /// </para>
    /// <para>
    /// ⚠️ The backfill therefore <b>arms two rules on upgrade</b>. Both are quiet on a healthy
    /// instance — a 30-second lock and an 80% full worker pool are incidents, not weather.
    /// RunnableTasks is deliberately left NULL: a defensible number depends on the core count
    /// and none has been measured yet.
    /// </para>
    /// </summary>
    public partial class PressureAlertThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BlockingDurationSecondsThreshold",
                table: "MssqlServerProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RunnableTasksAlertThreshold",
                table: "MssqlServerProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkerUtilizationAlertPercent",
                table: "MssqlServerProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE MssqlServerProfiles SET " +
                "BlockingDurationSecondsThreshold = 30, " +
                "WorkerUtilizationAlertPercent = 80;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockingDurationSecondsThreshold",
                table: "MssqlServerProfiles");

            migrationBuilder.DropColumn(
                name: "RunnableTasksAlertThreshold",
                table: "MssqlServerProfiles");

            migrationBuilder.DropColumn(
                name: "WorkerUtilizationAlertPercent",
                table: "MssqlServerProfiles");
        }
    }
}
