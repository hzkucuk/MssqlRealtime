using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Modules.Mssql.Alerts;
using MssqlRealtime.Modules.Mssql.Models;
using MssqlRealtime.Core.Privacy;

namespace MssqlRealtime.Tests;

public class MssqlAlertRulesTests
{
    private static ServerProfile Profile() => new()
    {
        Name = "Merkez SQL",
        CustomerName = "Acme",
        Host = "10.0.0.5",
        CpuAlertPercent = 85,
        MemoryAlertPercent = 90,
        BlockedSessionAlertThreshold = 1,
        LongRunningQuerySecondsThreshold = 30,
        SessionCountAlertThreshold = 200,
        SqlProcessMemoryAlertMb = null
    };

    private static SnapshotBuilder Online(ServerProfile profile, MachineResources? resources = null)
    {
        var builder = new SnapshotBuilder(profile) { Status = ServerStatus.Online };
        builder.Resources = resources;
        return builder;
    }

    [Fact]
    public void CpuOverThresholdIsBreached()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources { CpuPercent = 91 });

        var cpu = Assert.Single(MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full), c => c.RuleId == MssqlAlertRules.Cpu);

        Assert.True(cpu.IsBreached);
        Assert.Equal(91, cpu.Value);
        Assert.Equal(85, cpu.Threshold);
    }

    [Fact]
    public void CpuTenPointsOverThresholdIsCritical()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources { CpuPercent = 96 });

        var cpu = Assert.Single(MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full), c => c.RuleId == MssqlAlertRules.Cpu);

        Assert.Equal(Severity.Critical, cpu.Severity);
    }

    [Fact]
    public void StaleCpuSampleIsCalledOutInTheMessage()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources { CpuPercent = 95, CpuSampleAgeSeconds = 120 });

        var cpu = Assert.Single(MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full), c => c.RuleId == MssqlAlertRules.Cpu);

        // The user must not act on a two-minute-old number believing it is live.
        Assert.Contains("120", cpu.Message);
    }

    [Fact]
    public void NullThresholdDisablesTheRuleEntirely()
    {
        var profile = Profile();
        profile.CpuAlertPercent = null;
        var builder = Online(profile, new MachineResources { CpuPercent = 100 });

        Assert.DoesNotContain(MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full), c => c.RuleId == MssqlAlertRules.Cpu);
    }

    [Fact]
    public void MissingMeasurementDoesNotProduceAFalseAllClear()
    {
        var profile = Profile();
        // No resources probe result at all — e.g. the login lacks VIEW SERVER STATE.
        var builder = Online(profile, resources: null);

        var candidates = MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full);

        // Absent is not the same as fine: the rule is simply not evaluated.
        Assert.DoesNotContain(candidates, c => c.RuleId == MssqlAlertRules.Cpu);
        Assert.DoesNotContain(candidates, c => c.RuleId == MssqlAlertRules.Memory);
    }

    [Fact]
    public void OfflineServerReportsOnlyTheOfflineRule()
    {
        var profile = Profile();
        var builder = new SnapshotBuilder(profile)
        {
            Status = ServerStatus.Offline,
            ErrorMessage = "Sunucuya ulaşılamıyor."
        };

        var candidates = MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full);

        var offline = Assert.Single(candidates);
        Assert.Equal(MssqlAlertRules.Offline, offline.RuleId);
        Assert.True(offline.IsBreached);
        Assert.Equal(Severity.Critical, offline.Severity);

        // Crucially: no CPU/memory "not breached" candidates, which would have cleared alerts
        // that are still real — we simply cannot see the server.
        Assert.DoesNotContain(candidates, c => c.RuleId == MssqlAlertRules.Cpu);
    }

    [Fact]
    public void BlockingCountsDistinctBlockedSessions()
    {
        var profile = Profile();
        var builder = Online(profile);
        builder.Blocking =
        [
            new BlockingEdge { BlockedSessionId = 60, BlockingSessionId = 55, BlockingProgram = "Mikro" },
            new BlockingEdge { BlockedSessionId = 61, BlockingSessionId = 55, BlockingProgram = "Mikro" }
        ];

        var blocking = Assert.Single(MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full), c => c.RuleId == MssqlAlertRules.Blocking);

        Assert.True(blocking.IsBreached);
        Assert.Equal(2, blocking.Value);
        Assert.Contains("Mikro", blocking.Message);
    }

    [Fact]
    public void LongRunningUsesTheSlowestRequest()
    {
        var profile = Profile();
        var builder = Online(profile);
        builder.Requests =
        [
            new RequestInfo { SessionId = 70, ElapsedSeconds = 5 },
            new RequestInfo { SessionId = 71, ElapsedSeconds = 240, ProgramName = "Rapor" }
        ];

        var longRunning = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.LongRunning);

        Assert.True(longRunning.IsBreached);
        Assert.Equal(240, longRunning.Value);
        Assert.Equal(Severity.Critical, longRunning.Severity);
        Assert.Contains("71", longRunning.Message);
    }

    [Fact]
    public void HealthyServerReportsEveryRuleAsNotBreached()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources
        {
            CpuPercent = 20,
            MemoryUsedPercent = 55,
            AvailablePhysicalMemoryMb = 8000
        });
        builder.Sessions = [new SessionInfo { SessionId = 60 }];

        var candidates = MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full);

        // Every rule still reports in — that is what lets the engine clear a stale alert.
        Assert.All(candidates, c => Assert.False(c.IsBreached));
        Assert.Contains(candidates, c => c.RuleId == MssqlAlertRules.Cpu);
        Assert.Contains(candidates, c => c.RuleId == MssqlAlertRules.Memory);
        Assert.Contains(candidates, c => c.RuleId == MssqlAlertRules.SessionCount);
    }

    // --- Kilit süresi: sayı değil, süre ---

    [Fact]
    public void BlockingDurationUsesLongestWaitNotHeadCount()
    {
        var profile = Profile();
        var builder = Online(profile);

        // A single victim, stuck for 45 seconds. The head-count rule sees "1" and is calm;
        // this rule is the one that has to speak.
        builder.Blocking =
        [
            new BlockingEdge
            {
                BlockedSessionId = 60,
                BlockingSessionId = 55,
                WaitTimeMs = 45_000,
                BlockingProgram = "Mikro",
                BlockingSql = "UPDATE stok SET adet = adet - 1"
            }
        ];

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.BlockingDuration);

        Assert.True(rule.IsBreached);
        Assert.Equal(45, rule.Value);
        Assert.Equal(30, rule.Threshold);
        Assert.Contains("55", rule.Message);
        Assert.Contains("Mikro", rule.Message);
        Assert.Contains("UPDATE stok", rule.Context);
    }

    [Fact]
    public void BlockingDurationTakesTheWorstEdgeNotTheFirst()
    {
        var profile = Profile();
        var builder = Online(profile);
        builder.Blocking =
        [
            new BlockingEdge { BlockedSessionId = 60, BlockingSessionId = 55, WaitTimeMs = 2_000 },
            new BlockingEdge { BlockedSessionId = 61, BlockingSessionId = 55, WaitTimeMs = 130_000 }
        ];

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.BlockingDuration);

        Assert.Equal(130, rule.Value);
        Assert.Equal(Severity.Critical, rule.Severity);
    }

    [Fact]
    public void BlockingDurationStillReportsWhenNothingIsBlocked()
    {
        var profile = Profile();
        var builder = Online(profile);

        // Rule 4: a rule that goes quiet instead of reporting "not breached" leaves the
        // previous alert open forever.
        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.BlockingDuration);

        Assert.False(rule.IsBreached);
        Assert.Equal(0, rule.Value);
    }

    // --- Worker havuzu ---

    [Fact]
    public void WorkerUtilizationBreachesAsThePoolFillsUp()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources { ActiveWorkers = 500, MaxWorkers = 576 });

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.WorkerUtilization);

        Assert.True(rule.IsBreached);
        Assert.Equal(87, rule.Value);
        Assert.Equal(Severity.Warning, rule.Severity);
        Assert.Contains("500/576", rule.Message);
    }

    [Fact]
    public void WorkerUtilizationTurnsCriticalTenPointsOverTheLimit()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources { ActiveWorkers = 540, MaxWorkers = 576 });

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.WorkerUtilization);

        Assert.Equal(94, rule.Value);
        Assert.Equal(Severity.Critical, rule.Severity);
    }

    [Fact]
    public void WorkerUtilizationRuleIsDroppedWhenTheCeilingIsUnknown()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources { ActiveWorkers = 40, MaxWorkers = 0 });

        // Rule 3: an unmeasurable ratio leaves the list entirely. Reporting it as 0% — or as
        // "not breached" — would close an alert nobody actually verified.
        Assert.DoesNotContain(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.WorkerUtilization);
    }

    // --- İşlemci sırası ---

    [Fact]
    public void RunnableTasksRuleIsOffUntilSomeoneMeasuresTheServer()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources { RunnableTasks = 40, SchedulerCount = 8 });

        Assert.DoesNotContain(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.RunnableTasks);
    }

    // --- Tek olay, kaç bildirim? ---

    [Fact]
    public void OneBlockedQueryFiresBothTheDurationAndTheLongRunningRule()
    {
        var profile = Profile();
        var builder = Online(profile);

        // Tek bir olay: 45 saniyedir bloke olan tek bir istek. Bloke bir istek aynı zamanda
        // ÇALIŞAN bir istektir; total_elapsed_time işlemeye devam eder.
        builder.Blocking =
        [
            new BlockingEdge { BlockedSessionId = 60, BlockingSessionId = 55, WaitTimeMs = 45_000 }
        ];
        builder.Requests =
        [
            new RequestInfo { SessionId = 60, ElapsedSeconds = 45, BlockingSessionId = 55 }
        ];

        var breached = MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full)
            .Where(c => c.IsBreached)
            .Select(c => c.RuleId)
            .ToArray();

        Assert.Contains(MssqlAlertRules.BlockingDuration, breached);
        Assert.Contains(MssqlAlertRules.Blocking, breached);
        Assert.DoesNotContain(MssqlAlertRules.LongRunning, breached);
    }

    [Fact]
    public void ALongQueryThatIsNotBlockedStillFiresTheLongRunningRule()
    {
        var profile = Profile();
        var builder = Online(profile);
        builder.Requests = [new RequestInfo { SessionId = 71, ElapsedSeconds = 240, ProgramName = "Rapor" }];

        var longRunning = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.LongRunning);

        Assert.True(longRunning.IsBreached);
        Assert.Equal(240, longRunning.Value);
    }

    [Fact]
    public void WithTheDurationRuleOffALongBlockedQueryIsStillReported()
    {
        var profile = Profile();
        profile.BlockingDurationSecondsThreshold = null;
        var builder = Online(profile);
        builder.Blocking =
        [
            new BlockingEdge { BlockedSessionId = 60, BlockingSessionId = 55, WaitTimeMs = 45_000 }
        ];
        builder.Requests =
        [
            new RequestInfo { SessionId = 60, ElapsedSeconds = 45, BlockingSessionId = 55 }
        ];

        // Nobody else is watching duration now, so hiding it would lose the incident.
        var longRunning = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.LongRunning);

        Assert.True(longRunning.IsBreached);
    }

    // --- SQL metni: her kuralın bağlamında ---

    [Fact]
    public void LongRunningCarriesTheStatementNotJustTheSpid()
    {
        var profile = Profile();
        var builder = Online(profile);
        builder.Requests =
        [
            new RequestInfo
            {
                SessionId = 71,
                ElapsedSeconds = 240,
                ProgramName = "Rapor",
                SqlText = "SELECT * FROM satis_hareket WHERE tarih > @p0"
            }
        ];

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.LongRunning);

        Assert.Contains("SPID 71", rule.Context);
        Assert.Contains("Sorgu: SELECT * FROM satis_hareket", rule.Context);
    }

    [Fact]
    public void MaskedStorageKeepsTheAlertUsableWithoutTheValues()
    {
        var profile = Profile();
        var builder = Online(profile);
        builder.Sessions = [new SessionInfo { SessionId = 71, ProgramName = "RaporServisi" }];
        builder.Requests =
        [
            new RequestInfo
            {
                SessionId = 71,
                ElapsedSeconds = 120,
                SqlText = "SELECT * FROM Musteri WHERE TCKimlik = '12345678901'"
            }
        ];

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Masked),
            c => c.RuleId == MssqlAlertRules.LongRunning);

        // An alert record outlives the session it describes, so the value is what goes; who
        // ran it and which query stay, because that is what somebody acts on.
        Assert.DoesNotContain("12345678901", rule.Context);
        Assert.Contains("SPID 71", rule.Context);
        Assert.Contains("Sorgu: SELECT * FROM Musteri WHERE TCKimlik = ?", rule.Context);
    }

    [Fact]
    public void NoneStorageLeavesTheIdentityLineAndDropsTheStatement()
    {
        var profile = Profile();
        var builder = Online(profile);
        builder.Sessions = [new SessionInfo { SessionId = 71, ProgramName = "RaporServisi" }];
        builder.Requests =
        [
            new RequestInfo
            {
                SessionId = 71,
                ElapsedSeconds = 120,
                SqlText = "SELECT * FROM Musteri WHERE TCKimlik = '12345678901'"
            }
        ];

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.None),
            c => c.RuleId == MssqlAlertRules.LongRunning);

        Assert.DoesNotContain("Sorgu:", rule.Context);
        Assert.Contains("SPID 71", rule.Context);
        Assert.Contains("RaporServisi", rule.Context);
    }

    [Fact]
    public void CpuRuleCarriesTheStatementOfTheHeaviestSession()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources { CpuPercent = 95 });
        builder.Sessions =
        [
            new SessionInfo { SessionId = 60, CpuTimeMs = 10 },
            new SessionInfo { SessionId = 61, CpuTimeMs = 900_000, SqlText = "EXEC dbo.gece_kapanis" }
        ];

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.Cpu);

        Assert.Contains("SPID 61", rule.Context);
        Assert.Contains("Sorgu: EXEC dbo.gece_kapanis", rule.Context);
    }

    [Fact]
    public void BlockingCarriesWhatTheBlockerRan()
    {
        var profile = Profile();
        var builder = Online(profile);
        builder.Sessions = [new SessionInfo { SessionId = 55, ProgramName = "Mikro" }];
        builder.Blocking =
        [
            new BlockingEdge
            {
                BlockedSessionId = 60,
                BlockingSessionId = 55,
                WaitTimeMs = 5_000,
                BlockingSql = "UPDATE stok SET adet = 0"
            }
        ];

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.Blocking);

        Assert.Contains("SPID 55", rule.Context);
        Assert.Contains("Sorgu: UPDATE stok SET adet = 0", rule.Context);
    }

    [Fact]
    public void StatementIsFoldedOntoOneLineAndCut()
    {
        var profile = Profile();
        var builder = Online(profile);
        builder.Requests =
        [
            new RequestInfo
            {
                SessionId = 71,
                ElapsedSeconds = 240,
                SqlText = "SELECT\n\t*\nFROM   t\nWHERE x = " + new string('9', 500)
            }
        ];

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.LongRunning);

        Assert.DoesNotContain('\n', rule.Context!);
        Assert.DoesNotContain('\t', rule.Context!);
        Assert.Contains("SELECT * FROM t WHERE", rule.Context);
        Assert.EndsWith("…", rule.Context);

        // The store cuts context at 400 characters; the identity line has to survive that.
        Assert.True(rule.Context!.Length < 400, $"bağlam {rule.Context.Length} karakter");
    }

    [Fact]
    public void SessionWithoutStatementStillGetsAnIdentityLine()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources { CpuPercent = 95 });

        // An idle session with nothing open: the probe deliberately skips the text lookup.
        builder.Sessions = [new SessionInfo { SessionId = 61, CpuTimeMs = 900_000, ProgramName = "Mikro" }];

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.Cpu);

        Assert.Contains("SPID 61", rule.Context);
        Assert.DoesNotContain("Sorgu:", rule.Context);
    }

    [Fact]
    public void RunnableTasksNamesTheSchedulerCountSoTheNumberCanBeJudged()
    {
        var profile = Profile();
        profile.RunnableTasksAlertThreshold = 8;
        var builder = Online(profile, new MachineResources { RunnableTasks = 12, SchedulerCount = 8 });

        var rule = Assert.Single(
            MssqlAlertRules.Evaluate(profile, builder, StatementStorage.Full),
            c => c.RuleId == MssqlAlertRules.RunnableTasks);

        Assert.True(rule.IsBreached);
        Assert.Equal(12, rule.Value);
        Assert.Contains("8 zamanlayıcı", rule.Message);
    }
}
