using Dapper;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Probes;

/// <summary>
/// Machine CPU and RAM, read without installing anything on the customer's server.
/// <para>
/// ⚠️ Accuracy note, measured behaviour of SQL Server itself: the scheduler-monitor ring
/// buffer is written roughly <b>once per minute</b>, so CPU% is a recent sample, not an
/// instantaneous reading. We return its age (<see cref="MachineResources.CpuSampleAgeSeconds"/>)
/// rather than pretending it is live — an alert must not fire on a number whose age is unknown.
/// Memory, by contrast, is read live from sys.dm_os_sys_memory.
/// </para>
/// </summary>
public sealed class ResourcesProbe : ISqlProbe
{
    public string Name => "resources";
    public int Order => 15;

    private const string Sql = """
        -- 1) Machine CPU from the scheduler monitor ring buffer (latest sample + its age)
        SELECT TOP (1)
            r.SystemIdle                                              AS SystemIdlePercent,
            r.SqlProcessUtilization                                   AS SqlCpuPercent,
            (si.ms_ticks - r.[timestamp]) / 1000                      AS SampleAgeSeconds
        FROM (
            SELECT
                CONVERT(xml, record).value('(./Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int')          AS SystemIdle,
                CONVERT(xml, record).value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int')  AS SqlProcessUtilization,
                [timestamp]
            FROM sys.dm_os_ring_buffers
            WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR'
              AND record LIKE '%<SystemHealth>%'
        ) r
        CROSS JOIN sys.dm_os_sys_info si
        ORDER BY r.[timestamp] DESC;

        -- 2) Machine memory, live
        SELECT
            total_physical_memory_kb / 1024         AS TotalPhysicalMemoryMb,
            available_physical_memory_kb / 1024     AS AvailablePhysicalMemoryMb,
            system_memory_state_desc                AS SystemMemoryState
        FROM sys.dm_os_sys_memory;

        -- 3) What the SQL Server process itself holds
        SELECT
            physical_memory_in_use_kb / 1024        AS SqlProcessMemoryMb,
            memory_utilization_percentage           AS SqlMemoryUtilizationPercent
        FROM sys.dm_os_process_memory;

        -- 4) Target memory and page life expectancy (instance-name agnostic)
        SELECT
            MAX(CASE WHEN RTRIM(counter_name) = 'Target Server Memory (KB)' THEN cntr_value / 1024 END) AS TargetMemoryMb,
            MAX(CASE WHEN RTRIM(counter_name) = 'Page life expectancy'      THEN cntr_value END)        AS PageLifeExpectancy
        FROM sys.dm_os_performance_counters
        WHERE RTRIM(counter_name) IN ('Target Server Memory (KB)', 'Page life expectancy');

        -- 5) Scheduler pressure: tasks waiting for a CPU right now, and how much of the
        --    worker thread pool is already committed. Worker exhaustion is the failure that
        --    takes the instance away from us entirely: once every worker is busy, new
        --    connections queue on THREADPOOL and the monitor cannot log in either.
        --    active_workers_count is read from sys.dm_os_schedulers rather than from
        --    sys.dm_os_sys_info — the latter is where max_workers_count lives, and mixing
        --    the two is deliberate. Hidden schedulers (DAC, resource monitor) are excluded
        --    from the numerator but included in max_workers_count, so the ratio errs low
        --    by a handful of workers.
        SELECT
            COUNT(*)                             AS SchedulerCount,
            ISNULL(SUM(runnable_tasks_count), 0) AS RunnableTasks,
            ISNULL(SUM(active_workers_count), 0) AS ActiveWorkers,
            (SELECT max_workers_count FROM sys.dm_os_sys_info) AS MaxWorkers
        FROM sys.dm_os_schedulers
        WHERE status = 'VISIBLE ONLINE';
        """;

    public async Task ExecuteAsync(ProbeContext context, CancellationToken cancellationToken)
    {
        await using var multi = await context.Connection.QueryMultipleAsync(
            new CommandDefinition(Sql, commandTimeout: context.CommandTimeoutSeconds,
                cancellationToken: cancellationToken));

        var cpu = await multi.ReadFirstOrDefaultAsync<CpuRow>();
        var sysMem = await multi.ReadFirstOrDefaultAsync<SysMemoryRow>();
        var procMem = await multi.ReadFirstOrDefaultAsync<ProcessMemoryRow>();
        var counters = await multi.ReadFirstOrDefaultAsync<CounterRow>();
        var sched = await multi.ReadFirstOrDefaultAsync<SchedulerRow>();

        double? memoryUsedPercent = null;
        if (sysMem is { TotalPhysicalMemoryMb: > 0 })
        {
            var used = sysMem.TotalPhysicalMemoryMb - sysMem.AvailablePhysicalMemoryMb;
            memoryUsedPercent = Math.Round(used * 100.0 / sysMem.TotalPhysicalMemoryMb, 1);
        }

        context.Builder.Resources = new MachineResources
        {
            CpuPercent = cpu is null ? null : 100 - cpu.SystemIdlePercent,
            SqlCpuPercent = cpu?.SqlCpuPercent,
            CpuSampleAgeSeconds = cpu?.SampleAgeSeconds,
            TotalPhysicalMemoryMb = sysMem?.TotalPhysicalMemoryMb,
            AvailablePhysicalMemoryMb = sysMem?.AvailablePhysicalMemoryMb,
            MemoryUsedPercent = memoryUsedPercent,
            SystemMemoryState = sysMem?.SystemMemoryState,
            SqlProcessMemoryMb = procMem?.SqlProcessMemoryMb,
            SqlTargetMemoryMb = counters?.TargetMemoryMb,
            PageLifeExpectancySeconds = counters?.PageLifeExpectancy,
            SchedulerCount = sched?.SchedulerCount ?? 0,
            RunnableTasks = sched?.RunnableTasks ?? 0,
            ActiveWorkers = sched?.ActiveWorkers ?? 0,
            MaxWorkers = sched?.MaxWorkers ?? 0
        };
    }

    // Mutable property bags rather than positional records: Dapper matches a positional
    // record constructor by exact CLR type, and DMV columns are smallint/int/bigint in ways
    // that do not line up. With settable properties Dapper converts instead of throwing.
    private sealed class CpuRow
    {
        public int SystemIdlePercent { get; set; }
        public int SqlCpuPercent { get; set; }
        public long SampleAgeSeconds { get; set; }
    }

    private sealed class SysMemoryRow
    {
        public long TotalPhysicalMemoryMb { get; set; }
        public long AvailablePhysicalMemoryMb { get; set; }
        public string? SystemMemoryState { get; set; }
    }

    private sealed class ProcessMemoryRow
    {
        public long SqlProcessMemoryMb { get; set; }
        public int? SqlMemoryUtilizationPercent { get; set; }
    }

    private sealed class CounterRow
    {
        public long? TargetMemoryMb { get; set; }
        public int? PageLifeExpectancy { get; set; }
    }

    private sealed class SchedulerRow
    {
        public int SchedulerCount { get; set; }
        public int RunnableTasks { get; set; }
        public int ActiveWorkers { get; set; }
        public int MaxWorkers { get; set; }
    }
}
