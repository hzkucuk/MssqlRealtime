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

    // 🔴 Olculdu 2026-08-07 (musteri makinesi): surum, edisyon, cekirdek ve calisma suresi
    // TEK sorguda toplaniyordu ve sorgu iki DMV'ye baglıydı. O DMV'lerden biri eksik ya da
    // yetkisizse (sys.dm_os_host_info yalnizca SQL 2017+ vardir) sorgunun tamami dusuyor ve
    // SERVERPROPERTY'den gelen surum/edisyon da onunla birlikte kayboluyordu — ekranda her
    // sey tire. Artik iki parca: once her kurulumda calisan SERVERPROPERTY, sonra "olursa iyi"
    // sayilan DMV'ler. Ikincisi patlarsa birincisi ayakta kalir.
    private const string IdentitySql = """
        SELECT
            CONVERT(nvarchar(128), SERVERPROPERTY('ServerName'))     AS ServerName,
            CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS ProductVersion,
            CONVERT(nvarchar(128), SERVERPROPERTY('ProductLevel'))   AS ProductLevel,
            CONVERT(nvarchar(128), SERVERPROPERTY('Edition'))        AS Edition;
        """;

    private const string HostSql = """
        SELECT
            si.sqlserver_start_time                              AS StartedAt,
            DATEDIFF(minute, si.sqlserver_start_time, GETDATE()) AS UptimeMinutes,
            si.cpu_count                                         AS CpuCount,
            -- Measured 2026-08-04: SERVERPROPERTY('HostPlatform') returns NULL on SQL 2022;
            -- sys.dm_os_host_info is the view that actually carries it. It exists from
            -- SQL Server 2017 on, so its absence must not cost us the version.
            (SELECT TOP 1 host_platform FROM sys.dm_os_host_info) AS HostPlatform
        FROM sys.dm_os_sys_info si;
        """;

    public async Task ExecuteAsync(ProbeContext context, CancellationToken cancellationToken)
    {
        var identity = await context.Connection.QuerySingleOrDefaultAsync<Row>(
            new CommandDefinition(IdentitySql, commandTimeout: context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken));

        if (identity is null)
        {
            return;
        }

        Row? host = null;

        try
        {
            host = await context.Connection.QuerySingleOrDefaultAsync<Row>(
                new CommandDefinition(HostSql, commandTimeout: context.CommandTimeoutSeconds,
                    cancellationToken: cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cekirdek sayisi ve calisma suresi guzel ama zorunlu degil; surumu goturmesine
            // izin verilmez. Hata kaydedilir ki sessizce kaybolmasin.
            context.Builder.AddProbeError(Name, $"host bilgisi okunamadi: {ex.Message}");
        }

        context.Builder.Instance = new SqlInstanceInfo
        {
            ServerName = identity.ServerName,
            ProductVersion = identity.ProductVersion,
            ProductLevel = identity.ProductLevel,
            Edition = identity.Edition,
            StartedAt = host?.StartedAt is null ? null : new DateTimeOffset(host.StartedAt.Value, TimeSpan.Zero),
            UptimeMinutes = host?.UptimeMinutes,
            CpuCount = host?.CpuCount,
            HostPlatform = host?.HostPlatform
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
