using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Tests;

public class SnapshotBuilderTests
{
    private static ServerProfile Profile() => new() { Name = "Merkez SQL", CustomerName = "Acme", Host = "10.0.0.5" };

    [Fact]
    public void SummaryCountsBlockedSessionsAndHeadBlockers()
    {
        var builder = new SnapshotBuilder(Profile()) { Status = ServerStatus.Online };
        builder.Sessions =
        [
            new SessionInfo { SessionId = 55 },
            new SessionInfo { SessionId = 60 },
            new SessionInfo { SessionId = 61 }
        ];
        builder.Blocking =
        [
            new BlockingEdge { BlockedSessionId = 60, BlockingSessionId = 55 },
            new BlockingEdge { BlockedSessionId = 61, BlockingSessionId = 60 }
        ];

        var snapshot = builder.Build(DateTimeOffset.UtcNow, 12, []);

        Assert.Equal(2, snapshot.Summary.BlockedSessions);
        // 55 blocks but is not blocked; 60 is both, so it is not the head of the chain.
        Assert.Equal(1, snapshot.Summary.BlockingHeads);
    }

    [Fact]
    public void SeverityComesFromActiveAlertsNotRawNumbers()
    {
        var builder = new SnapshotBuilder(Profile()) { Status = ServerStatus.Online };

        var alert = new AlertState
        {
            Target = new AlertTarget { ModuleId = "mssql", TargetId = "1", TargetName = "Merkez SQL" },
            RuleId = "cpu",
            RuleTitle = "İşlemci",
            Severity = Severity.Critical,
            Message = "İşlemci %98",
            SinceUtc = DateTimeOffset.UtcNow
        };

        var snapshot = builder.Build(DateTimeOffset.UtcNow, 10, [alert]);

        Assert.Equal(Severity.Critical, snapshot.Summary.Severity);
    }

    [Fact]
    public void OfflineSnapshotIsCriticalEvenWithNoAlerts()
    {
        var builder = new SnapshotBuilder(Profile()) { Status = ServerStatus.Offline };

        var snapshot = builder.Build(DateTimeOffset.UtcNow, 5, []);

        Assert.Equal(Severity.Critical, snapshot.Summary.Severity);
        Assert.Equal(0, snapshot.Summary.TotalSessions);
    }

    [Fact]
    public void CarryForwardKeepsValuesFromThrottledProbes()
    {
        var previousBuilder = new SnapshotBuilder(Profile()) { Status = ServerStatus.Online };
        previousBuilder.Instance = new SqlInstanceInfo { ProductVersion = "16.0.4252.3" };
        previousBuilder.Databases = [new DatabaseInfo { Name = "MikroDB_V17" }];
        var previous = previousBuilder.Build(DateTimeOffset.UtcNow, 20, []);

        // This round the expensive probes did not run.
        var builder = new SnapshotBuilder(Profile()) { Status = ServerStatus.Online };
        builder.CarryForwardFrom(previous);

        var snapshot = builder.Build(DateTimeOffset.UtcNow, 8, []);

        Assert.Equal("16.0.4252.3", snapshot.Instance?.ProductVersion);
        Assert.Single(snapshot.Databases);
    }

    [Fact]
    public void ProbeErrorsSurfaceWithoutFailingTheSnapshot()
    {
        var builder = new SnapshotBuilder(Profile()) { Status = ServerStatus.Online };
        builder.Sessions = [new SessionInfo { SessionId = 60 }];
        builder.AddProbeError("services", "VIEW SERVER STATE izni yok");

        var snapshot = builder.Build(DateTimeOffset.UtcNow, 15, []);

        // The snapshot still carries the data that did come back.
        Assert.Equal(ServerStatus.Online, snapshot.Status);
        Assert.Single(snapshot.Sessions);
        Assert.Contains("services", snapshot.ErrorMessage);
    }

    [Fact]
    public void DistinctApplicationsIgnoresCaseAndBlanks()
    {
        var builder = new SnapshotBuilder(Profile()) { Status = ServerStatus.Online };
        builder.Sessions =
        [
            new SessionInfo { SessionId = 60, ProgramName = "Mikro" },
            new SessionInfo { SessionId = 61, ProgramName = "mikro" },
            new SessionInfo { SessionId = 62, ProgramName = "  " },
            new SessionInfo { SessionId = 63, ProgramName = "Excel" }
        ];

        var snapshot = builder.Build(DateTimeOffset.UtcNow, 10, []);

        Assert.Equal(2, snapshot.Summary.DistinctApplications);
    }
}
