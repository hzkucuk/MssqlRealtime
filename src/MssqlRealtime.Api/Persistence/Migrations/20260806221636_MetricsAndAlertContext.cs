using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MssqlRealtime.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MetricsAndAlertContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Context",
                table: "AlertRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MetricSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModuleId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TakenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Resolution = table.Column<int>(type: "INTEGER", nullable: false),
                    CpuPercent = table.Column<double>(type: "REAL", nullable: true),
                    SqlCpuPercent = table.Column<double>(type: "REAL", nullable: true),
                    MemoryPercent = table.Column<double>(type: "REAL", nullable: true),
                    SqlMemoryMb = table.Column<int>(type: "INTEGER", nullable: true),
                    SessionCount = table.Column<int>(type: "INTEGER", nullable: true),
                    RequestCount = table.Column<int>(type: "INTEGER", nullable: true),
                    BlockedCount = table.Column<int>(type: "INTEGER", nullable: true),
                    LongestQuerySeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    SampleCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricSamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetricSamples_ModuleId_TargetId_Resolution_TakenAtUtc",
                table: "MetricSamples",
                columns: new[] { "ModuleId", "TargetId", "Resolution", "TakenAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetricSamples");

            migrationBuilder.DropColumn(
                name: "Context",
                table: "AlertRecords");
        }
    }
}
