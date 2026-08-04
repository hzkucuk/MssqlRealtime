using MssqlRealtime.Core.Alerts;

namespace MssqlRealtime.Modules.Mssql.Models;

/// <summary>
/// Mutable accumulator the probes write into; frozen into an immutable
/// <see cref="ServerSnapshot"/> once the poll completes.
/// </summary>
public sealed class SnapshotBuilder(ServerProfile profile)
{
    private readonly List<string> _probeErrors = [];

    public ServerProfile Profile { get; } = profile;
    public ServerStatus Status { get; set; } = ServerStatus.Unknown;
    public string? ErrorMessage { get; set; }

    public MachineResources? Resources { get; set; }
    public SqlInstanceInfo? Instance { get; set; }
    public IReadOnlyList<SessionInfo> Sessions { get; set; } = [];
    public IReadOnlyList<RequestInfo> Requests { get; set; } = [];
    public IReadOnlyList<BlockingEdge> Blocking { get; set; } = [];
    public IReadOnlyList<WaitStat> TopWaits { get; set; } = [];
    public IReadOnlyList<DatabaseInfo> Databases { get; set; } = [];
    public IReadOnlyList<SqlServiceInfo> Services { get; set; } = [];

    public IReadOnlyList<string> ProbeErrors => _probeErrors;

    public void AddProbeError(string probeName, string message) =>
        _probeErrors.Add($"{probeName}: {message}");

    /// <summary>
    /// Carries forward values a throttled probe did not refresh this round, so the mobile
    /// card never flickers back to "—" between two expensive polls.
    /// </summary>
    public void CarryForwardFrom(ServerSnapshot previous)
    {
        Instance ??= previous.Instance;
        if (Databases.Count == 0) Databases = previous.Databases;
        if (Services.Count == 0) Services = previous.Services;
        if (TopWaits.Count == 0) TopWaits = previous.TopWaits;
    }

    public ServerSnapshot Build(DateTimeOffset capturedAt, int collectionMs, IReadOnlyList<AlertState> activeAlerts)
    {
        var summary = BuildSummary();

        // The card's colour is decided by the alerts, not by the raw numbers: the user's
        // thresholds are the only definition of "bad" this product has.
        if (activeAlerts.Count > 0)
        {
            summary = summary with { Severity = activeAlerts.Max(a => a.Severity) };
        }

        var error = ErrorMessage;
        if (error is null && _probeErrors.Count > 0)
        {
            error = string.Join(" | ", _probeErrors);
        }

        return new ServerSnapshot
        {
            ServerId = Profile.Id,
            ServerName = Profile.Name,
            CustomerName = Profile.CustomerName,
            CapturedAt = capturedAt,
            Status = Status,
            CollectionMs = collectionMs,
            ErrorMessage = error,
            Summary = summary,
            Resources = Resources,
            Instance = Instance,
            Sessions = Sessions,
            Requests = Requests,
            Blocking = Blocking,
            TopWaits = TopWaits,
            Databases = Databases,
            Services = Services,
            ActiveAlerts = activeAlerts
        };
    }

    private ServerSummary BuildSummary()
    {
        if (Status != ServerStatus.Online)
        {
            return new ServerSummary { Severity = Severity.Critical };
        }

        var blockedIds = Blocking.Select(b => b.BlockedSessionId).ToHashSet();
        var blockingIds = Blocking.Select(b => b.BlockingSessionId).ToHashSet();

        return new ServerSummary
        {
            TotalSessions = Sessions.Count,
            UserSessions = Sessions.Count(s => s.SessionId > 50),
            ActiveRequests = Requests.Count,
            BlockedSessions = blockedIds.Count,
            // A head blocker is blocking someone while not itself being blocked.
            BlockingHeads = blockingIds.Except(blockedIds).Count(),
            LongestRunningSeconds = Requests.Count == 0 ? 0 : Requests.Max(r => r.ElapsedSeconds),
            OpenTransactions = Sessions.Sum(s => s.OpenTransactionCount),
            CpuPercent = Resources?.CpuPercent,
            MemoryUsedPercent = Resources?.MemoryUsedPercent,
            TopWaitType = TopWaits.Count > 0 ? TopWaits[0].WaitType : null,
            DistinctApplications = Sessions
                .Where(s => !string.IsNullOrWhiteSpace(s.ProgramName))
                .Select(s => s.ProgramName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            DistinctHosts = Sessions
                .Where(s => !string.IsNullOrWhiteSpace(s.HostName))
                .Select(s => s.HostName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            Severity = Severity.Ok
        };
    }
}
