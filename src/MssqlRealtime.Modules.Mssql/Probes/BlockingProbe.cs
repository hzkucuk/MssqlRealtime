using Dapper;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Modules.Mssql.Probes;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Probes;

/// <summary>
/// Blocking chains. The blocker is usually asleep with an open transaction and therefore has
/// no row in sys.dm_exec_requests at all — so its statement is recovered from
/// sys.dm_exec_connections.most_recent_sql_handle, which is the whole point of this probe.
/// </summary>
public sealed class BlockingProbe : ISqlProbe
{
    public string Name => "blocking";
    public int Order => 40;

    private const string Sql = """
        SELECT
            r.session_id                        AS BlockedSessionId,
            r.blocking_session_id               AS BlockingSessionId,
            r.wait_time                         AS WaitTimeMs,
            r.wait_type                         AS WaitType,
            NULLIF(r.wait_resource, '')         AS WaitResource,
            blocked_s.program_name              AS BlockedProgram,
            blocker_s.program_name              AS BlockingProgram,
            blocked_s.login_name                AS BlockedLogin,
            blocker_s.login_name                AS BlockingLogin,
            LEFT(blocked_t.text, @MaxLen)       AS BlockedSql,
            LEFT(blocker_t.text, @MaxLen)       AS BlockingSql
        FROM sys.dm_exec_requests r
        INNER JOIN sys.dm_exec_sessions blocked_s ON blocked_s.session_id = r.session_id
        LEFT JOIN sys.dm_exec_sessions blocker_s ON blocker_s.session_id = r.blocking_session_id
        LEFT JOIN sys.dm_exec_connections blocker_c ON blocker_c.session_id = r.blocking_session_id
        OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) blocked_t
        OUTER APPLY sys.dm_exec_sql_text(blocker_c.most_recent_sql_handle) blocker_t
        WHERE r.blocking_session_id <> 0
          AND r.session_id <> r.blocking_session_id;
        """;

    public async Task ExecuteAsync(ProbeContext context, CancellationToken cancellationToken)
    {
        var rows = await context.Connection.QueryAsync<Row>(
            new CommandDefinition(Sql, new { MaxLen = RequestsProbe.SqlTextMaxLength },
                commandTimeout: context.CommandTimeoutSeconds, cancellationToken: cancellationToken));

        var edges = rows.Select(r => new BlockingEdge
        {
            BlockedSessionId = r.BlockedSessionId,
            BlockingSessionId = r.BlockingSessionId,
            WaitTimeMs = r.WaitTimeMs,
            WaitType = r.WaitType,
            WaitResource = r.WaitResource,
            BlockedProgram = r.BlockedProgram?.Trim(),
            BlockingProgram = r.BlockingProgram?.Trim(),
            BlockedLogin = r.BlockedLogin,
            BlockingLogin = r.BlockingLogin,
            BlockedSql = r.BlockedSql?.Trim(),
            BlockingSql = r.BlockingSql?.Trim()
        }).ToList();

        context.Builder.Blocking = edges;

        if (edges.Count == 0)
        {
            return;
        }

        // Flag the sessions involved so the session list can be coloured without re-joining.
        var blocked = edges.Select(e => e.BlockedSessionId).ToHashSet();
        var blockers = edges.Select(e => e.BlockingSessionId).ToHashSet();

        context.Builder.Sessions = context.Builder.Sessions
            .Select(s => s with
            {
                IsBlocked = blocked.Contains(s.SessionId),
                IsBlocker = blockers.Contains(s.SessionId)
            })
            .ToList();
    }

    private sealed class Row
    {
        public int BlockedSessionId { get; set; }
        public int BlockingSessionId { get; set; }
        public int WaitTimeMs { get; set; }
        public string? WaitType { get; set; }
        public string? WaitResource { get; set; }
        public string? BlockedProgram { get; set; }
        public string? BlockingProgram { get; set; }
        public string? BlockedLogin { get; set; }
        public string? BlockingLogin { get; set; }
        public string? BlockedSql { get; set; }
        public string? BlockingSql { get; set; }
    }
}
