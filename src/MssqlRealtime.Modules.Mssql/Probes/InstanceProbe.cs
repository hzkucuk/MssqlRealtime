using Dapper;
using MssqlRealtime.Core.Abstractions;
using MssqlRealtime.Modules.Mssql.Probes;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Probes;

/// <summary>
/// Static-ish facts about the instance: version, edition, uptime, core count.
/// Cheap but pointless to re-read every 5 seconds, so it is throttled.
/// </summary>
public sealed class InstanceProbe : ISqlProbe
{
    public string Name => "instance";
    public int Order => 10;
    public int EveryNthPoll => 60;

    private const string Sql = """
        SELECT
            CONVERT(nvarchar(128), SERVERPROPERTY('ServerName'))     AS ServerName,
            CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS ProductVersion,
            CONVERT(nvarchar(128), SERVERPROPERTY('ProductLevel'))   AS ProductLevel,
            CONVERT(nvarchar(128), SERVERPROPERTY('Edition'))        AS Edition,
            si.sqlserver_start_time                                  AS StartedAt,
            DATEDIFF(minute, si.sqlserver_start_time, GETDATE())     AS UptimeMinutes,
            si.cpu_count                                             AS CpuCount,
            -- Measured 2026-08-04: SERVERPROPERTY('HostPlatform') returns NULL on SQL 2022;
            -- sys.dm_os_host_info is the view that actually carries it.
            hi.host_platform                                         AS HostPlatform
        FROM sys.dm_os_sys_info si
        CROSS JOIN sys.dm_os_host_info hi;
        """;

    public async Task ExecuteAsync(ProbeContext context, CancellationToken cancellationToken)
    {
        var row = await context.Connection.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(Sql, commandTimeout: context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken));

        if (row is null)
        {
            return;
        }

        context.Builder.Instance = new SqlInstanceInfo
        {
            ServerName = row.ServerName,
            ProductVersion = row.ProductVersion,
            ProductLevel = row.ProductLevel,
            Edition = row.Edition,
            StartedAt = row.StartedAt is null ? null : new DateTimeOffset(row.StartedAt.Value, TimeSpan.Zero),
            UptimeMinutes = row.UptimeMinutes,
            CpuCount = row.CpuCount,
            HostPlatform = row.HostPlatform
        };
    }

    private sealed class Row
    {
        public string? ServerName { get; set; }
        public string? ProductVersion { get; set; }
        public string? ProductLevel { get; set; }
        public string? Edition { get; set; }
        public DateTime? StartedAt { get; set; }
        public int? UptimeMinutes { get; set; }
        public int? CpuCount { get; set; }
        public string? HostPlatform { get; set; }
    }
}
