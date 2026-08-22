using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MssqlRealtime.Api.Persistence.Migrations
{
    /// <summary>
    /// Data-only migration: no schema change. The session-count alert default moved from 200
    /// to 500 in v0.20.1, but a C# property initializer only applies to newly created rows —
    /// servers already in the database would silently keep alerting at 200.
    /// <para>
    /// Only rows still holding the old default are touched. NULL (rule switched off) and any
    /// hand-picked value are left alone: an explicit choice by the user is not ours to change.
    /// </para>
    /// </summary>
    public partial class RaiseSessionCountThresholdDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE MssqlServerProfiles SET SessionCountAlertThreshold = 500 " +
                "WHERE SessionCountAlertThreshold = 200;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lossy by nature: a server the user deliberately set to 500 is pushed back to 200.
            // The old value is not recoverable — this migration carries no history of it.
            migrationBuilder.Sql(
                "UPDATE MssqlServerProfiles SET SessionCountAlertThreshold = 200 " +
                "WHERE SessionCountAlertThreshold = 500;");
        }
    }
}
