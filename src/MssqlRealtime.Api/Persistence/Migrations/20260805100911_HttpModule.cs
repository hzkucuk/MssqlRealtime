using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MssqlRealtime.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HttpModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HttpTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GroupName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ExpectedStatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpectedBodyContains = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CheckIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    IgnoreCertificateErrors = table.Column<bool>(type: "INTEGER", nullable: false),
                    AlertOnDown = table.Column<bool>(type: "INTEGER", nullable: false),
                    SlowResponseMs = table.Column<int>(type: "INTEGER", nullable: true),
                    CertificateExpiryWarningDays = table.Column<int>(type: "INTEGER", nullable: true),
                    AlertConsecutiveBreaches = table.Column<int>(type: "INTEGER", nullable: false),
                    AlertRenotifyMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HttpTargets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HttpTargets_GroupName",
                table: "HttpTargets",
                column: "GroupName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HttpTargets");
        }
    }
}
