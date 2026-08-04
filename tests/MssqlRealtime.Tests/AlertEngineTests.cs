using MssqlRealtime.Core.Alerts;

namespace MssqlRealtime.Tests;

/// <summary>
/// The engine decides what reaches a phone at 03:00, so these tests are about restraint as
/// much as about detection.
/// </summary>
public class AlertEngineTests
{
    private static readonly AlertTarget Target = new()
    {
        ModuleId = "mssql",
        TargetId = "server-1",
        TargetName = "Merkez SQL",
        GroupName = "Acme"
    };

    private static AlertCandidate Cpu(bool breached, Severity severity = Severity.Warning, int consecutive = 3) => new()
    {
        RuleId = "cpu",
        RuleTitle = "İşlemci",
        IsBreached = breached,
        Severity = breached ? severity : Severity.Ok,
        Message = "İşlemci %95 — sınır %85",
        Value = 95,
        Threshold = 85,
        RequiredConsecutiveBreaches = consecutive,
        RenotifyMinutes = 15
    };

    [Fact]
    public void SingleSpikeDoesNotNotify()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        // One breached cycle out of a required three: a 5-second blip must stay silent.
        var outcome = engine.Evaluate(Target, [Cpu(true)], now);

        Assert.Empty(outcome.ToNotify);
        Assert.Empty(outcome.Active);
    }

    [Fact]
    public void FiresOnlyAfterRequiredConsecutiveBreaches()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        engine.Evaluate(Target, [Cpu(true)], now);
        engine.Evaluate(Target, [Cpu(true)], now.AddSeconds(5));
        var third = engine.Evaluate(Target, [Cpu(true)], now.AddSeconds(10));

        var notification = Assert.Single(third.ToNotify);
        Assert.False(notification.IsCleared);
        Assert.Equal("cpu", notification.Alert.RuleId);
        Assert.Single(third.Active);
    }

    [Fact]
    public void RecoveryInsideGraceWindowResetsTheCounter()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        engine.Evaluate(Target, [Cpu(true)], now);
        engine.Evaluate(Target, [Cpu(true)], now.AddSeconds(5));
        engine.Evaluate(Target, [Cpu(false)], now.AddSeconds(10));
        var afterRecovery = engine.Evaluate(Target, [Cpu(true)], now.AddSeconds(15));

        // Counting restarted, so this is breach 1 of 3 — nothing fires.
        Assert.Empty(afterRecovery.ToNotify);
    }

    [Fact]
    public void DoesNotRenotifyWhileStillFiringInsideTheWindow()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            engine.Evaluate(Target, [Cpu(true)], now.AddSeconds(i * 5));
        }

        var later = engine.Evaluate(Target, [Cpu(true)], now.AddMinutes(5));

        Assert.Empty(later.ToNotify);
        Assert.Single(later.Active);
    }

    [Fact]
    public void RenotifiesAfterTheWindowElapses()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            engine.Evaluate(Target, [Cpu(true)], now.AddSeconds(i * 5));
        }

        var later = engine.Evaluate(Target, [Cpu(true)], now.AddMinutes(16));

        Assert.Single(later.ToNotify);
    }

    [Fact]
    public void EscalationNotifiesImmediately()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            engine.Evaluate(Target, [Cpu(true)], now.AddSeconds(i * 5));
        }

        // Warning → Critical is news even though the renotify window has not passed.
        var escalated = engine.Evaluate(Target, [Cpu(true, Severity.Critical)], now.AddMinutes(1));

        var notification = Assert.Single(escalated.ToNotify);
        Assert.Equal(Severity.Critical, notification.Alert.Severity);
    }

    [Fact]
    public void ClearingNotifiesOnlyIfTheUserWasToldAboutIt()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            engine.Evaluate(Target, [Cpu(true)], now.AddSeconds(i * 5));
        }

        var cleared = engine.Evaluate(Target, [Cpu(false)], now.AddMinutes(2));

        var notification = Assert.Single(cleared.ToNotify);
        Assert.True(notification.IsCleared);
        Assert.Empty(cleared.Active);
    }

    [Fact]
    public void ImmediateRuleFiresOnTheFirstBreach()
    {
        var engine = new AlertEngine();

        var outcome = engine.Evaluate(Target, [Cpu(true, consecutive: 1)], DateTimeOffset.UtcNow);

        Assert.Single(outcome.ToNotify);
    }

    [Fact]
    public void ForgetDropsStateSoAReAddedServerStartsClean()
    {
        var engine = new AlertEngine();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            engine.Evaluate(Target, [Cpu(true)], now.AddSeconds(i * 5));
        }

        engine.Forget(Target.ModuleId, Target.TargetId);

        Assert.Empty(engine.GetActive());

        // And a recovery afterwards must not produce a "back to normal" for an alert the
        // user can no longer see.
        var afterForget = engine.Evaluate(Target, [Cpu(false)], now.AddMinutes(1));
        Assert.Empty(afterForget.ToNotify);
    }

    [Fact]
    public void RulesAreTrackedPerTargetIndependently()
    {
        var engine = new AlertEngine();
        var other = Target with { TargetId = "server-2", TargetName = "Şube SQL" };
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            engine.Evaluate(Target, [Cpu(true, consecutive: 3)], now.AddSeconds(i * 5));
        }

        var otherServer = engine.Evaluate(other, [Cpu(true, consecutive: 3)], now.AddSeconds(20));

        Assert.Empty(otherServer.ToNotify);
        Assert.Single(engine.GetActive());
    }
}
