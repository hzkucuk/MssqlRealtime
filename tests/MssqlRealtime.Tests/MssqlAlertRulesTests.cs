using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Modules.Mssql.Alerts;
using MssqlRealtime.Modules.Mssql.Models;

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

        var cpu = Assert.Single(MssqlAlertRules.Evaluate(profile, builder), c => c.RuleId == MssqlAlertRules.Cpu);

        Assert.True(cpu.IsBreached);
        Assert.Equal(91, cpu.Value);
        Assert.Equal(85, cpu.Threshold);
    }

    [Fact]
    public void CpuTenPointsOverThresholdIsCritical()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources { CpuPercent = 96 });

        var cpu = Assert.Single(MssqlAlertRules.Evaluate(profile, builder), c => c.RuleId == MssqlAlertRules.Cpu);

        Assert.Equal(Severity.Critical, cpu.Severity);
    }

    [Fact]
    public void StaleCpuSampleIsCalledOutInTheMessage()
    {
        var profile = Profile();
        var builder = Online(profile, new MachineResources { CpuPercent = 95, CpuSampleAgeSeconds = 120 });

        var cpu = Assert.Single(MssqlAlertRules.Evaluate(profile, builder), c => c.RuleId == MssqlAlertRules.Cpu);

        // The user must not act on a two-minute-old number believing it is live.
        Assert.Contains("120", cpu.Message);
    }

    [Fact]
    public void NullThresholdDisablesTheRuleEntirely()
    {
        var profile = Profile();
        profile.CpuAlertPercent = null;
        var builder = Online(profile, new MachineResources { CpuPercent = 100 });

        Assert.DoesNotContain(MssqlAlertRules.Evaluate(profile, builder), c => c.RuleId == MssqlAlertRules.Cpu);
    }

    [Fact]
    public void MissingMeasurementDoesNotProduceAFalseAllClear()
    {
        var profile = Profile();
        // No resources probe result at all — e.g. the login lacks VIEW SERVER STATE.
        var builder = Online(profile, resources: null);

        var candidates = MssqlAlertRules.Evaluate(profile, builder);

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

        var candidates = MssqlAlertRules.Evaluate(profile, builder);

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

        var blocking = Assert.Single(MssqlAlertRules.Evaluate(profile, builder), c => c.RuleId == MssqlAlertRules.Blocking);

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
            MssqlAlertRules.Evaluate(profile, builder),
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

        var candidates = MssqlAlertRules.Evaluate(profile, builder);

        // Every rule still reports in — that is what lets the engine clear a stale alert.
        Assert.All(candidates, c => Assert.False(c.IsBreached));
        Assert.Contains(candidates, c => c.RuleId == MssqlAlertRules.Cpu);
        Assert.Contains(candidates, c => c.RuleId == MssqlAlertRules.Memory);
        Assert.Contains(candidates, c => c.RuleId == MssqlAlertRules.SessionCount);
    }
}
