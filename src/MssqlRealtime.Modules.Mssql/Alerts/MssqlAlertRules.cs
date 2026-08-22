using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Alerts;

/// <summary>
/// Turns one snapshot plus the user's thresholds into rule evaluations. Every rule is
/// evaluated on every cycle — including the ones that pass — because the engine needs to
/// see recovery to clear an alert and tell the user it is over.
/// <para>
/// Adding a rule: add a case here and a threshold property on <see cref="ServerProfile"/>.
/// Debouncing and renotify suppression come from the engine and need no work here.
/// </para>
/// </summary>
public static class MssqlAlertRules
{
    public const string Cpu = "cpu";
    public const string Memory = "memory";
    public const string SqlProcessMemory = "sql-memory";
    public const string Blocking = "blocking";
    public const string BlockingDuration = "blocking-duration";
    public const string LongRunning = "long-running";
    public const string SessionCount = "session-count";
    public const string RunnableTasks = "runnable-tasks";
    public const string WorkerUtilization = "worker-utilization";
    public const string Offline = "offline";


    /// <summary>
    /// One line naming the heaviest consumer at this instant: SPID, application, login and
    /// machine. Captured while the rule fires, because ten minutes later — when someone reads
    /// the notification — the session is usually gone.
    /// </summary>
    private static string? Describe(SessionInfo? session, string? measure = null)
    {
        if (session is null)
        {
            return null;
        }

        var parts = new List<string> { $"SPID {session.SessionId}" };

        if (!string.IsNullOrWhiteSpace(session.ProgramName)) parts.Add(session.ProgramName!);
        if (!string.IsNullOrWhiteSpace(session.LoginName)) parts.Add(session.LoginName!);
        if (!string.IsNullOrWhiteSpace(session.HostName)) parts.Add(session.HostName!);
        if (!string.IsNullOrWhiteSpace(measure)) parts.Add(measure!);

        return string.Join(" · ", parts);
    }

    /// <summary>The session burning the most CPU right now, ignoring system sessions.</summary>
    private static SessionInfo? TopCpu(SnapshotBuilder builder) =>
        builder.Sessions.Where(x => x.SessionId > 50).MaxBy(x => x.CpuTimeMs);

    private static SessionInfo? TopMemory(SnapshotBuilder builder) =>
        builder.Sessions.Where(x => x.SessionId > 50).MaxBy(x => x.MemoryUsageKb);

    public static IReadOnlyList<AlertCandidate> Evaluate(ServerProfile profile, SnapshotBuilder builder)
    {
        var candidates = new List<AlertCandidate>();

        // --- Reachability. Everything else is meaningless while this one is firing. ---
        if (profile.AlertOnOffline)
        {
            var offline = builder.Status is ServerStatus.Offline or ServerStatus.Error;
            candidates.Add(offline
                ? new AlertCandidate
                {
                    RuleId = Offline,
                    RuleTitle = "Erişilemiyor",
                    IsBreached = true,
                    Severity = Severity.Critical,
                    Message = builder.ErrorMessage ?? "Sunucuya bağlanılamıyor.",
                    RequiredConsecutiveBreaches = 2,
                    RenotifyMinutes = profile.AlertRenotifyMinutes
                }
                : AlertCandidate.Ok(Offline, "Erişilemiyor"));
        }

        // A server we cannot reach has no metrics; reporting them as "fine" would clear
        // alerts that were never actually resolved.
        if (builder.Status != ServerStatus.Online)
        {
            return candidates;
        }

        var resources = builder.Resources;

        if (profile.CpuAlertPercent is { } cpuLimit && resources?.CpuPercent is { } cpu)
        {
            var age = resources.CpuSampleAgeSeconds;
            var ageNote = age is > 90 ? $" (ölçüm {age} sn önce)" : string.Empty;

            candidates.Add(new AlertCandidate
            {
                RuleId = Cpu,
                RuleTitle = "İşlemci",
                IsBreached = cpu >= cpuLimit,
                Severity = cpu >= Math.Min(100, cpuLimit + 10) ? Severity.Critical : Severity.Warning,
                Message = $"İşlemci %{cpu:0} — sınır %{cpuLimit}{ageNote}",
                Value = cpu,
                Threshold = cpuLimit,
                Unit = "%",
                Context = Describe(TopCpu(builder), $"CPU {TopCpu(builder)?.CpuTimeMs:N0} ms"),
                RequiredConsecutiveBreaches = profile.AlertConsecutiveBreaches,
                RenotifyMinutes = profile.AlertRenotifyMinutes
            });
        }

        if (profile.MemoryAlertPercent is { } memLimit && resources?.MemoryUsedPercent is { } mem)
        {
            var free = resources.AvailablePhysicalMemoryMb;
            var freeNote = free is null ? string.Empty : $", boşta {free} MB";

            candidates.Add(new AlertCandidate
            {
                RuleId = Memory,
                RuleTitle = "Bellek",
                IsBreached = mem >= memLimit,
                Severity = mem >= Math.Min(100, memLimit + 5) ? Severity.Critical : Severity.Warning,
                Message = $"Bellek %{mem:0.0} kullanımda — sınır %{memLimit}{freeNote}",
                Value = mem,
                Threshold = memLimit,
                Unit = "%",
                Context = Describe(TopMemory(builder), $"{TopMemory(builder)?.MemoryUsageKb:N0} KB"),
                RequiredConsecutiveBreaches = profile.AlertConsecutiveBreaches,
                RenotifyMinutes = profile.AlertRenotifyMinutes
            });
        }

        if (profile.SqlProcessMemoryAlertMb is { } sqlMemLimit && resources?.SqlProcessMemoryMb is { } sqlMem)
        {
            candidates.Add(new AlertCandidate
            {
                RuleId = SqlProcessMemory,
                RuleTitle = "SQL Server belleği",
                IsBreached = sqlMem >= sqlMemLimit,
                Severity = Severity.Warning,
                Message = $"SQL Server {sqlMem} MB bellek kullanıyor — sınır {sqlMemLimit} MB",
                Value = sqlMem,
                Threshold = sqlMemLimit,
                Unit = "MB",
                Context = Describe(TopMemory(builder), $"{TopMemory(builder)?.MemoryUsageKb:N0} KB"),
                RequiredConsecutiveBreaches = profile.AlertConsecutiveBreaches,
                RenotifyMinutes = profile.AlertRenotifyMinutes
            });
        }

        if (profile.BlockedSessionAlertThreshold is { } blockLimit)
        {
            var blocked = builder.Blocking.Select(b => b.BlockedSessionId).Distinct().Count();
            var head = builder.Blocking.Count > 0
                ? builder.Blocking
                    .Select(b => b.BlockingProgram)
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))
                : null;

            candidates.Add(new AlertCandidate
            {
                RuleId = Blocking,
                RuleTitle = "Kilitlenme",
                IsBreached = blocked >= blockLimit && blocked > 0,
                Severity = blocked >= blockLimit * 3 ? Severity.Critical : Severity.Warning,
                Message = head is null
                    ? $"{blocked} oturum bloke durumda."
                    : $"{blocked} oturum bloke durumda — engelleyen: {head}",
                Value = blocked,
                Threshold = blockLimit,
                Context = Describe(
                    builder.Sessions.FirstOrDefault(x =>
                        x.SessionId == builder.Blocking.Select(b => b.BlockingSessionId).FirstOrDefault()),
                    "engelleyen"),
                RequiredConsecutiveBreaches = profile.AlertConsecutiveBreaches,
                RenotifyMinutes = profile.AlertRenotifyMinutes
            });
        }

        if (profile.LongRunningQuerySecondsThreshold is { } longLimit)
        {
            var longest = builder.Requests.Count == 0 ? null : builder.Requests.MaxBy(r => r.ElapsedSeconds);
            var seconds = longest?.ElapsedSeconds ?? 0;

            candidates.Add(new AlertCandidate
            {
                RuleId = LongRunning,
                RuleTitle = "Uzun süren sorgu",
                IsBreached = seconds >= longLimit,
                Severity = seconds >= longLimit * 4 ? Severity.Critical : Severity.Warning,
                Message = longest is null
                    ? string.Empty
                    : $"{seconds} sn süren sorgu — session {longest.SessionId}, {longest.ProgramName ?? "bilinmeyen uygulama"}",
                Value = seconds,
                Threshold = longLimit,
                Unit = "sn",
                Context = longest is null
                    ? null
                    : string.Join(" · ", new[]
                        {
                            $"SPID {longest.SessionId}",
                            longest.ProgramName,
                            longest.LoginName,
                            longest.HostName,
                            $"{seconds} sn"
                        }.Where(x => !string.IsNullOrWhiteSpace(x))),
                RequiredConsecutiveBreaches = profile.AlertConsecutiveBreaches,
                RenotifyMinutes = profile.AlertRenotifyMinutes
            });
        }

        if (profile.SessionCountAlertThreshold is { } sessionLimit)
        {
            var count = builder.Sessions.Count;
            candidates.Add(new AlertCandidate
            {
                RuleId = SessionCount,
                RuleTitle = "Oturum sayısı",
                IsBreached = count >= sessionLimit,
                Severity = Severity.Warning,
                Message = $"{count} açık oturum — sınır {sessionLimit}",
                Value = count,
                Threshold = sessionLimit,
                RequiredConsecutiveBreaches = profile.AlertConsecutiveBreaches,
                RenotifyMinutes = profile.AlertRenotifyMinutes
            });
        }

        // --- How long one victim has been stuck, not how many victims there are. ---
        // Ten sessions blocked for half a second is a busy server; one blocked for two
        // minutes is an incident. The head-count rule above cannot tell those apart.
        if (profile.BlockingDurationSecondsThreshold is { } blockSecondsLimit)
        {
            var longest = builder.Blocking.Count == 0 ? null : builder.Blocking.MaxBy(b => b.WaitTimeMs);
            var seconds = (longest?.WaitTimeMs ?? 0) / 1000;

            candidates.Add(new AlertCandidate
            {
                RuleId = BlockingDuration,
                RuleTitle = "Kilit süresi",
                IsBreached = seconds >= blockSecondsLimit && longest is not null,
                Severity = seconds >= blockSecondsLimit * 4 ? Severity.Critical : Severity.Warning,
                Message = longest is null
                    ? string.Empty
                    : $"{seconds} sn süren kilit — SPID {longest.BlockedSessionId} bekliyor, "
                      + $"engelleyen SPID {longest.BlockingSessionId}"
                      + (string.IsNullOrWhiteSpace(longest.BlockingProgram)
                          ? string.Empty
                          : $" ({longest.BlockingProgram})"),
                Value = seconds,
                Threshold = blockSecondsLimit,
                Context = longest?.BlockingSql is { Length: > 0 } sql
                    ? $"Engelleyen sorgu: {sql}"
                    : null,
                RequiredConsecutiveBreaches = profile.AlertConsecutiveBreaches,
                RenotifyMinutes = profile.AlertRenotifyMinutes
            });
        }

        // --- CPU queue depth. Unlike the ring-buffer CPU%, this one is live. ---
        if (profile.RunnableTasksAlertThreshold is { } runnableLimit && resources is not null)
        {
            var runnable = resources.RunnableTasks;
            var schedulers = resources.SchedulerCount;

            candidates.Add(new AlertCandidate
            {
                RuleId = RunnableTasks,
                RuleTitle = "İşlemci sırası",
                IsBreached = runnable >= runnableLimit,
                Severity = runnable >= runnableLimit * 3 ? Severity.Critical : Severity.Warning,
                Message = schedulers > 0
                    ? $"{runnable} görev işlemci sırasında bekliyor ({schedulers} zamanlayıcı) — sınır {runnableLimit}"
                    : $"{runnable} görev işlemci sırasında bekliyor — sınır {runnableLimit}",
                Value = runnable,
                Threshold = runnableLimit,
                Context = Describe(TopCpu(builder), $"{TopCpu(builder)?.CpuTimeMs:N0} ms CPU"),
                RequiredConsecutiveBreaches = profile.AlertConsecutiveBreaches,
                RenotifyMinutes = profile.AlertRenotifyMinutes
            });
        }

        // --- Worker exhaustion. The one failure that also locks the monitor out. ---
        // Guarded on WorkerUtilizationPercent rather than on resources alone: when the probe
        // could not read max_workers_count the ratio is unknowable, and reporting an
        // unmeasured rule as "not breached" would clear an alert nobody verified (rule 3).
        if (profile.WorkerUtilizationAlertPercent is { } workerLimit
            && resources?.WorkerUtilizationPercent is { } workerPercent)
        {
            candidates.Add(new AlertCandidate
            {
                RuleId = WorkerUtilization,
                RuleTitle = "Worker thread doluluğu",
                IsBreached = workerPercent >= workerLimit,
                Severity = workerPercent >= Math.Min(100, workerLimit + 10)
                    ? Severity.Critical
                    : Severity.Warning,
                Message = $"Worker havuzu %{workerPercent} dolu — "
                          + $"{resources.ActiveWorkers}/{resources.MaxWorkers}, sınır %{workerLimit}",
                Value = workerPercent,
                Threshold = workerLimit,
                Context = "Havuz dolduğunda yeni bağlantılar THREADPOOL beklemesine girer; "
                          + "izleme paneli de bağlanamaz.",
                RequiredConsecutiveBreaches = profile.AlertConsecutiveBreaches,
                RenotifyMinutes = profile.AlertRenotifyMinutes
            });
        }

        return candidates;
    }
}
