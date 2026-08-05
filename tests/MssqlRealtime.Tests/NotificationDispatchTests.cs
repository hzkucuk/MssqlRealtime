using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Notifications;
using MssqlRealtime.Infrastructure.Notifications;

namespace MssqlRealtime.Tests;

/// <summary>
/// Which notifications a channel should receive. Getting this wrong either wakes someone at
/// 03:00 for a warning they asked not to see, or silently drops the alert that mattered.
/// </summary>
public class NotificationDispatchTests
{
    private static AlertNotification Notification(Severity severity, bool cleared = false) => new()
    {
        Alert = new AlertState
        {
            Target = new AlertTarget { ModuleId = "mssql", TargetId = "1", TargetName = "Merkez SQL" },
            RuleId = "cpu",
            RuleTitle = "İşlemci",
            Severity = severity,
            Message = "İşlemci %95",
            SinceUtc = DateTimeOffset.UtcNow
        },
        IsCleared = cleared,
        RaisedAtUtc = DateTimeOffset.UtcNow
    };

    private static ChannelConfiguration Configuration(
        bool enabled = true,
        Severity minimum = Severity.Warning,
        bool recoveries = true) => new()
    {
        ChannelId = "telegram",
        Enabled = enabled,
        MinimumSeverity = minimum,
        SendRecoveries = recoveries,
        Settings = new ChannelSettings(new Dictionary<string, string>())
    };

    [Fact]
    public void DisabledChannelReceivesNothing()
    {
        Assert.False(NotificationDispatcher.ShouldDeliver(
            Configuration(enabled: false), Notification(Severity.Critical)));
    }

    [Fact]
    public void UnconfiguredChannelReceivesNothing()
    {
        Assert.False(NotificationDispatcher.ShouldDeliver(null, Notification(Severity.Critical)));
    }

    [Fact]
    public void CriticalOnlyChannelIgnoresWarnings()
    {
        var configuration = Configuration(minimum: Severity.Critical);

        Assert.False(NotificationDispatcher.ShouldDeliver(configuration, Notification(Severity.Warning)));
        Assert.True(NotificationDispatcher.ShouldDeliver(configuration, Notification(Severity.Critical)));
    }

    [Fact]
    public void RecoveriesCanBeSwitchedOffIndependently()
    {
        var quiet = Configuration(recoveries: false);

        Assert.False(NotificationDispatcher.ShouldDeliver(quiet, Notification(Severity.Warning, cleared: true)));
        Assert.True(NotificationDispatcher.ShouldDeliver(quiet, Notification(Severity.Warning)));
    }

    [Fact]
    public void RecoveryIgnoresTheSeverityFloor()
    {
        // The recovery of a critical alert carries Severity.Critical on the alert itself, but
        // what decides delivery here is SendRecoveries — a "critical only" channel should
        // still be told the critical alert ended.
        var configuration = Configuration(minimum: Severity.Critical);

        Assert.True(NotificationDispatcher.ShouldDeliver(
            configuration, Notification(Severity.Warning, cleared: true)));
    }

    [Fact]
    public void SeverityZeroChannelReceivesEverything()
    {
        var configuration = Configuration(minimum: Severity.Ok);

        Assert.True(NotificationDispatcher.ShouldDeliver(configuration, Notification(Severity.Warning)));
        Assert.True(NotificationDispatcher.ShouldDeliver(configuration, Notification(Severity.Critical)));
    }
}
