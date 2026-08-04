using Dapper;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Probes;

/// <summary>
/// Per-database facts that change slowly: size, recovery model, RCSI, last full backup.
/// Throttled — this is context for the operator, not a live metric.
/// </summary>
public sealed class DatabasesProbe : ISqlProbe
{
    public string Name => "databases";
    public int Order => 70;
    public int EveryNthPoll => 60;

    private const string Sql = """
        SELECT
            d.name                                  AS Name,
            d.state_desc                            AS State,
            d.recovery_model_desc                   AS RecoveryModel,
            sizes.DataSizeMb                        AS DataSizeMb,
            sizes.LogSizeMb                         AS LogSizeMb,
            d.is_read_committed_snapshot_on         AS IsReadCommittedSnapshotOn,
            bk.LastFullBackup                       AS LastFullBackup
        FROM sys.databases d
        OUTER APPLY (
            SELECT
                SUM(CASE WHEN mf.type = 0 THEN mf.size END) / 128 AS DataSizeMb,
                SUM(CASE WHEN mf.type = 1 THEN mf.size END) / 128 AS LogSizeMb
            FROM sys.master_files mf
            WHERE mf.database_id = d.database_id
        ) sizes
        OUTER APPLY (
            SELECT MAX(b.backup_finish_date) AS LastFullBackup
            FROM msdb.dbo.backupset b
            WHERE b.database_name = d.name
              AND b.type = 'D'
        ) bk
        WHERE d.database_id > 4              -- skip master/model/msdb/tempdb
           OR d.database_id = 2              -- but keep tempdb: it is an operational signal
        ORDER BY sizes.DataSizeMb DESC;
        """;

    public async Task ExecuteAsync(ProbeContext context, CancellationToken cancellationToken)
    {
        var rows = await context.Connection.QueryAsync<Row>(
            new CommandDefinition(Sql, commandTimeout: context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken));

        context.Builder.Databases = rows.Select(r => new DatabaseInfo
        {
            Name = r.Name,
            State = r.State,
            RecoveryModel = r.RecoveryModel,
            DataSizeMb = r.DataSizeMb,
            LogSizeMb = r.LogSizeMb,
            IsReadCommittedSnapshotOn = r.IsReadCommittedSnapshotOn,
            LastFullBackup = r.LastFullBackup is null ? null : new DateTimeOffset(r.LastFullBackup.Value, TimeSpan.Zero)
        }).ToList();
    }

    private sealed class Row
    {
        public string Name { get; set; } = string.Empty;
        public string? State { get; set; }
        public string? RecoveryModel { get; set; }
        public long? DataSizeMb { get; set; }
        public long? LogSizeMb { get; set; }
        public bool IsReadCommittedSnapshotOn { get; set; }
        public DateTime? LastFullBackup { get; set; }
    }
}
