using Dapper;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Probes;

/// <summary>
/// Which Windows account each SQL Server service runs under, and whether it is set to start
/// automatically. Needs VIEW SERVER STATE; if the login lacks it the poller records a probe
/// error and the rest of the snapshot is still delivered.
/// </summary>
public sealed class ServicesProbe : ISqlProbe
{
    public string Name => "services";
    public int Order => 80;
    public int EveryNthPoll => 60;

    private const string Sql = """
        SELECT
            servicename         AS ServiceName,
            service_account     AS ServiceAccount,
            status_desc         AS StatusDescription,
            startup_type_desc   AS StartupType,
            last_startup_time   AS LastStartupTime
        FROM sys.dm_server_services;
        """;

    public async Task ExecuteAsync(ProbeContext context, CancellationToken cancellationToken)
    {
        var rows = await context.Connection.QueryAsync<Row>(
            new CommandDefinition(Sql, commandTimeout: context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken));

        context.Builder.Services = rows.Select(r => new SqlServiceInfo
        {
            ServiceName = r.ServiceName,
            ServiceAccount = r.ServiceAccount,
            StatusDescription = r.StatusDescription,
            StartupType = r.StartupType,
            LastStartupTime = r.LastStartupTime is null
                ? null
                : new DateTimeOffset(r.LastStartupTime.Value.DateTime, TimeSpan.Zero)
        }).ToList();
    }

    private sealed class Row
    {
        public string ServiceName { get; set; } = string.Empty;
        public string? ServiceAccount { get; set; }
        public string? StatusDescription { get; set; }
        public string? StartupType { get; set; }
        public DateTimeOffset? LastStartupTime { get; set; }
    }
}
