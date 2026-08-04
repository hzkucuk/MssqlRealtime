using Dapper;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Modules.Mssql.Probes;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Probes;

/// <summary>
/// What is running right now, with the exact statement rather than the whole batch.
/// </summary>
public sealed class RequestsProbe : ISqlProbe
{
    public string Name => "requests";
    public int Order => 30;

    /// <summary>Statement text is truncated before it ever leaves the monitored server.</summary>
    public const int SqlTextMaxLength = 4000;

    private const string Sql = """
        SELECT
            r.session_id                                        AS SessionId,
            r.status                                            AS Status,
            r.command                                           AS Command,
            DB_NAME(r.database_id)                              AS DatabaseName,
            s.login_name                                        AS LoginName,
            s.host_name                                         AS HostName,
            s.program_name                                      AS ProgramName,
            r.total_elapsed_time / 1000                         AS ElapsedSeconds,
            r.cpu_time                                          AS CpuTimeMs,
            r.logical_reads                                     AS LogicalReads,
            NULLIF(r.blocking_session_id, 0)                    AS BlockingSessionId,
            r.wait_type                                         AS WaitType,
            r.wait_resource                                     AS WaitResource,
            r.wait_time                                         AS WaitTimeMs,
            NULLIF(CONVERT(int, r.percent_complete), 0)         AS PercentComplete,
            LEFT(
                SUBSTRING(
                    t.text,
                    (r.statement_start_offset / 2) + 1,
                    ((CASE r.statement_end_offset
                        WHEN -1 THEN DATALENGTH(t.text)
                        ELSE r.statement_end_offset
                      END - r.statement_start_offset) / 2) + 1),
                @MaxLen)                                        AS SqlText
        FROM sys.dm_exec_requests r
        INNER JOIN sys.dm_exec_sessions s ON s.session_id = r.session_id
        OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t
        WHERE s.is_user_process = 1
          AND r.session_id <> @@SPID
        ORDER BY r.total_elapsed_time DESC;
        """;

    public async Task ExecuteAsync(ProbeContext context, CancellationToken cancellationToken)
    {
        var rows = await context.Connection.QueryAsync<Row>(
            new CommandDefinition(Sql, new { MaxLen = SqlTextMaxLength },
                commandTimeout: context.CommandTimeoutSeconds, cancellationToken: cancellationToken));

        context.Builder.Requests = rows.Select(r => new RequestInfo
        {
            SessionId = r.SessionId,
            Status = r.Status,
            Command = r.Command,
            DatabaseName = r.DatabaseName,
            LoginName = r.LoginName,
            HostName = r.HostName,
            ProgramName = r.ProgramName?.Trim(),
            ElapsedSeconds = r.ElapsedSeconds,
            CpuTimeMs = r.CpuTimeMs,
            LogicalReads = r.LogicalReads,
            BlockingSessionId = r.BlockingSessionId,
            WaitType = r.WaitType,
            WaitResource = string.IsNullOrWhiteSpace(r.WaitResource) ? null : r.WaitResource,
            WaitTimeMs = r.WaitTimeMs,
            PercentComplete = r.PercentComplete,
            SqlText = r.SqlText?.Trim()
        }).ToList();
    }

    private sealed class Row
    {
        public int SessionId { get; set; }
        public string? Status { get; set; }
        public string? Command { get; set; }
        public string? DatabaseName { get; set; }
        public string? LoginName { get; set; }
        public string? HostName { get; set; }
        public string? ProgramName { get; set; }
        public int ElapsedSeconds { get; set; }
        public long CpuTimeMs { get; set; }
        public long LogicalReads { get; set; }
        public int? BlockingSessionId { get; set; }
        public string? WaitType { get; set; }
        public string? WaitResource { get; set; }
        public int WaitTimeMs { get; set; }
        public int? PercentComplete { get; set; }
        public string? SqlText { get; set; }
    }
}
