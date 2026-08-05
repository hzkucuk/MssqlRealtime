using MssqlRealtime.Core.Alerts;

namespace MssqlRealtime.Tests;

/// <summary>
/// Restart behaviour. Getting this wrong is not a subtle bug: it means every deployment
/// re-announces every ongoing problem to everyone's phone.
/// </summary>
public class AlertRestoreTests
{
    private static readonly AlertTarget Target = new()
    {
        ModuleId = "mssql",
        TargetId = "server-1",
        TargetName = "Merkez SQL",
        GroupName = "Acme"
    };

    private static AlertCandidate Cpu(bool breached = true, int consecutive = 3) => new()
    {
        RuleId = "cpu",
        RuleTitle = "İşlemci",
        IsBreached = breached,
        Severity = breached ? Severity.Warning : Severity.Ok,
        Message = "İşlemci %95 — sınır %85",
        Value = 95,
        Threshold = 85,
        RequiredConsecutiveBreaches = consecutive,
        RenotifyMinutes = 15
    };

    private static AlertState Persisted(DateTimeOffset since, DateTimeOffset? notified) => new()
    {
        Target = Target,
        RuleId = "cpu",
        RuleTitle = "İşlemci",
        Severity = Severity.Warning,
        Message = "İşlemci %95 — sınır %85",
        Value = 95,
        Threshold = 85,
        SinceUtc = since,
        LastNotifiedUtc = notified
    };

    [Fact]
    public void RestoredAlertIsNotAnnouncedAgain()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        // The service restarted; this alert was already firing and already notified.
        engine.Restore([Persisted(now.AddHours(-2), now.AddMinutes(-3))]);

        var afterRestart = engine.Evaluate(Target, [Cpu()], now);

        Assert.Empty(afterRestart.ToNotify);
        Assert.Single(afterRestart.Active);
    }

    [Fact]
    public void RestoredAlertKeepsItsOriginalStartTime()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;
        var startedAt = now.AddHours(-6);

        engine.Restore([Persisted(startedAt, now.AddMinutes(-1))]);

        var active = Assert.Single(engine.Evaluate(Target, [Cpu()], now).Active);

        // "6 saattir sürüyor" must survive a restart, otherwise the duration resets to zero
        // and an old problem looks new.
        Assert.Equal(startedAt, active.SinceUtc);
    }

    [Fact]
    public void RestoredAlertFiresImmediatelyWithoutWaitingForTheGraceWindowAgain()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        engine.Restore([Persisted(now.AddHours(-1), now.AddMinutes(-2))]);

        // One breached reading is enough: the breach was already confirmed before the restart.
        var outcome = engine.Evaluate(Target, [Cpu(consecutive: 10)], now);

        Assert.Single(outcome.Active);
    }

    [Fact]
    public void RestoredAlertStillRenotifiesOnceTheWindowPasses()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        engine.Restore([Persisted(now.AddHours(-2), now.AddMinutes(-20))]);

        var outcome = engine.Evaluate(Target, [Cpu()], now);

        Assert.Single(outcome.ToNotify);
    }

    [Fact]
    public void RestoredAlertThatHasRecoveredIsClearedAndAnnounced()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        engine.Restore([Persisted(now.AddHours(-2), now.AddMinutes(-5))]);

        var outcome = engine.Evaluate(Target, [Cpu(breached: false)], now);

        var notification = Assert.Single(outcome.ToNotify);
        Assert.True(notification.IsCleared);
        Assert.Empty(outcome.Active);
    }

    [Fact]
    public void RestoredAlertThatWasNeverNotifiedDoesNotAnnounceItsRecovery()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        // Persisted without LastNotifiedUtc: the user was never told, so the recovery is not news.
        engine.Restore([Persisted(now.AddHours(-2), notified: null)]);

        var outcome = engine.Evaluate(Target, [Cpu(breached: false)], now);

        Assert.Empty(outcome.ToNotify);
    }
}
