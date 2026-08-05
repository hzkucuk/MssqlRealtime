using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Notifications;

namespace MssqlRealtime.Tests;

public class NotificationChannelTests
{
    private static AlertNotification Notification(Severity severity, bool cleared = false) => new()
    {
        Alert = new AlertState
        {
            Target = new AlertTarget
            {
                ModuleId = "mssql",
                TargetId = "server-1",
                TargetName = "Merkez SQL",
                GroupName = "Acme"
            },
            RuleId = "cpu",
            RuleTitle = "İşlemci",
            Severity = severity,
            Message = "İşlemci %95 — sınır %85",
            Value = 95,
            Threshold = 85,
            Unit = "%",
            SinceUtc = DateTimeOffset.UtcNow
        },
        IsCleared = cleared,
        RaisedAtUtc = DateTimeOffset.UtcNow
    };

    [Theory]
    [InlineData(Severity.Warning, Severity.Warning, true)]
    [InlineData(Severity.Critical, Severity.Warning, true)]
    [InlineData(Severity.Warning, Severity.Critical, false)]
    [InlineData(Severity.Critical, Severity.Critical, true)]
    public void SeverityFilterDecidesDelivery(Severity alertSeverity, Severity minimum, bool shouldDeliver)
    {
        var notification = Notification(alertSeverity);

        // Mirrors the dispatcher's rule: a "critical only" channel must ignore warnings.
        var delivered = notification.Alert.Severity >= minimum;

        Assert.Equal(shouldDeliver, delivered);
    }

    [Fact]
    public void ChannelSettingsTreatBlankValuesAsMissing()
    {
        var settings = new ChannelSettings(new Dictionary<string, string>
        {
            ["botToken"] = "abc",
            ["chatId"] = "   "
        });

        Assert.Equal("abc", settings.Get("botToken"));
        Assert.Null(settings.Get("chatId"));
        Assert.False(settings.Has("botToken", "chatId"));
    }

    [Fact]
    public void ChannelSettingsRequireThrowsOnMissingKey()
    {
        var settings = new ChannelSettings(new Dictionary<string, string>());

        Assert.Throws<InvalidOperationException>(() => settings.Require("botToken"));
    }

    [Fact]
    public void ClearedNotificationCarriesRecoveryText()
    {
        var notification = Notification(Severity.Warning, cleared: true);

        Assert.Contains("normale döndü", notification.Body);
        Assert.StartsWith("✅", notification.Title);
    }

    [Fact]
    public void CriticalAndWarningGetDistinctTitles()
    {
        Assert.StartsWith("🔴", Notification(Severity.Critical).Title);
        Assert.StartsWith("🟠", Notification(Severity.Warning).Title);
    }
}
