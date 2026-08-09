using MssqlRealtime.Core.Alerts;

namespace MssqlRealtime.Modules.Mssql.Models;

public enum ServerStatus
{
    Unknown = 0,
    Online = 1,
    Offline = 2,
    Error = 3
}

/// <summary>
/// Everything the poller collected in one pass. This is the payload broadcast over SignalR.
/// New probes add a property here; old clients ignore what they do not know.
/// </summary>
public sealed record ServerSnapshot
{
    public required Guid ServerId { get; init; }
    public required string ServerName { get; init; }
    public required string CustomerName { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required ServerStatus Status { get; init; }

    /// <summary>Round-trip time of the probe batch — the cheapest health signal we have.</summary>
    public int CollectionMs { get; init; }

    public string? ErrorMessage { get; init; }

    public ServerSummary Summary { get; init; } = new();
    public MachineResources? Resources { get; init; }
    public SqlInstanceInfo? Instance { get; init; }

    public IReadOnlyList<SessionInfo> Sessions { get; init; } = [];
    public IReadOnlyList<RequestInfo> Requests { get; init; } = [];
    public IReadOnlyList<BlockingEdge> Blocking { get; init; } = [];
    public IReadOnlyList<WaitStat> TopWaits { get; init; } = [];
    public IReadOnlyList<DatabaseInfo> Databases { get; init; } = [];
    public IReadOnlyList<SqlServiceInfo> Services { get; init; } = [];

    /// <summary>Alerts that are currently active for this server.</summary>
    public IReadOnlyList<AlertState> ActiveAlerts { get; init; } = [];
}

/// <summary>
/// The mobile card. Deliberately small: this is what a phone shows in a list of servers.
/// </summary>
public sealed record ServerSummary
{
    public int TotalSessions { get; init; }
    public int UserSessions { get; init; }
    public int ActiveRequests { get; init; }
    public int BlockedSessions { get; init; }
    public int BlockingHeads { get; init; }
    public int LongestRunningSeconds { get; init; }
    public int OpenTransactions { get; init; }
    public double? CpuPercent { get; init; }
    public double? MemoryUsedPercent { get; init; }
    public string? TopWaitType { get; init; }

    /// <summary>Distinct client applications currently connected (program_name).</summary>
    public int DistinctApplications { get; init; }
    public int DistinctHosts { get; init; }

    public Severity Severity { get; init; } = Severity.Ok;
}

/// <summary>
/// Machine-wide resources as SQL Server sees them. Read without an agent:
/// CPU from the scheduler-monitor ring buffer, memory from sys.dm_os_sys_memory.
/// </summary>
public sealed record MachineResources
{
    /// <summary>Total machine CPU % (100 - SystemIdle) from the last ring-buffer sample.</summary>
    public double? CpuPercent { get; init; }

    /// <summary>The share of CPU attributable to the SQL Server process itself.</summary>
    public double? SqlCpuPercent { get; init; }

    /// <summary>
    /// How old the CPU sample is. SQL Server writes the scheduler-monitor ring buffer about
    /// once a minute, so this is shown next to the value instead of implying it is live.
    /// </summary>
    public long? CpuSampleAgeSeconds { get; init; }

    public long? TotalPhysicalMemoryMb { get; init; }
    public long? AvailablePhysicalMemoryMb { get; init; }
    public double? MemoryUsedPercent { get; init; }

    /// <summary>sqlservr.exe committed/physical memory.</summary>
    public long? SqlProcessMemoryMb { get; init; }
    public long? SqlTargetMemoryMb { get; init; }

    /// <summary>Windows memory pressure signal: Available / Low / Steady / High.</summary>
    public string? SystemMemoryState { get; init; }

    public int? PageLifeExpectancySeconds { get; init; }
    public int SchedulerCount { get; init; }
    public int RunnableTasks { get; init; }
}

public sealed record SqlInstanceInfo
{
    public string? ServerName { get; init; }
    public string? ProductVersion { get; init; }
    public string? ProductLevel { get; init; }
    public string? Edition { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public int? UptimeMinutes { get; init; }
    public int? CpuCount { get; init; }
    public string? HostPlatform { get; init; }
}

public sealed record SessionInfo
{
    public required int SessionId { get; init; }
    public string? LoginName { get; init; }
    public string? HostName { get; init; }
    public string? ProgramName { get; init; }
    public string? ClientAddress { get; init; }
    public string? Status { get; init; }
    public string? DatabaseName { get; init; }
    public DateTimeOffset? LoginTime { get; init; }
    public DateTimeOffset? LastRequestEnd { get; init; }
    public long CpuTimeMs { get; init; }
    public long Reads { get; init; }
    public long Writes { get; init; }
    public long LogicalReads { get; init; }
    public long MemoryUsageKb { get; init; }
    public int OpenTransactionCount { get; init; }
    public bool IsBlocked { get; init; }
    public bool IsBlocker { get; init; }
    public int IdleSeconds { get; init; }
}

public sealed record RequestInfo
{
    public required int SessionId { get; init; }

    /// <summary>
    /// Distinguishes concurrent requests of one session (MARS). SessionId on its own is
    /// not unique here, and the UI needs a stable unique key per row.
    /// </summary>
    public int RequestId { get; init; }
    public string? Status { get; init; }
    public string? Command { get; init; }
    public string? DatabaseName { get; init; }
    public string? LoginName { get; init; }
    public string? HostName { get; init; }
    public string? ProgramName { get; init; }
    public int ElapsedSeconds { get; init; }
    public long CpuTimeMs { get; init; }
    public long LogicalReads { get; init; }
    public int? BlockingSessionId { get; init; }
    public string? WaitType { get; init; }
    public string? WaitResource { get; init; }
    public int WaitTimeMs { get; init; }
    public int? PercentComplete { get; init; }
    public string? SqlText { get; init; }
}

/// <summary>One "A is blocked by B" edge; the client builds the tree from these.</summary>
public sealed record BlockingEdge
{
    public required int BlockedSessionId { get; init; }

    /// <summary>
    /// Distinguishes the blocked requests of one session (MARS). Same reason as
    /// <see cref="RequestInfo.RequestId"/>: the UI needs a stable unique key per row.
    /// </summary>
    public int BlockedRequestId { get; init; }
    public required int BlockingSessionId { get; init; }
    public int WaitTimeMs { get; init; }
    public string? WaitType { get; init; }
    public string? WaitResource { get; init; }
    public string? BlockedProgram { get; init; }
    public string? BlockingProgram { get; init; }
    public string? BlockedLogin { get; init; }
    public string? BlockingLogin { get; init; }
    public string? BlockedSql { get; init; }
    public string? BlockingSql { get; init; }
}

public sealed record WaitStat
{
    public required string WaitType { get; init; }
    public long WaitTimeMs { get; init; }
    public long WaitingTasks { get; init; }
    public double Percentage { get; init; }
}

public sealed record DatabaseInfo
{
    public required string Name { get; init; }
    public string? State { get; init; }
    public string? RecoveryModel { get; init; }
    public long? DataSizeMb { get; init; }
    public long? LogSizeMb { get; init; }
    public DateTimeOffset? LastFullBackup { get; init; }
    public bool IsReadCommittedSnapshotOn { get; init; }
}

public sealed record SqlServiceInfo
{
    public required string ServiceName { get; init; }
    public string? ServiceAccount { get; init; }
    public string? StatusDescription { get; init; }
    public string? StartupType { get; init; }
    public DateTimeOffset? LastStartupTime { get; init; }
}
