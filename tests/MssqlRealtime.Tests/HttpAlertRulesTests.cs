using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Modules.Http;
using MssqlRealtime.Modules.Http.Models;

namespace MssqlRealtime.Tests;

/// <summary>
/// The HTTP module follows the same rule contract as the MSSQL one; these tests exist partly
/// to keep that contract honest as more tools are added.
/// </summary>
public class HttpAlertRulesTests
{
    private static HttpTarget Target() => new()
    {
        Name = "Müşteri sitesi",
        GroupName = "Acme",
        Url = "https://ornek.com",
        AlertOnDown = true,
        SlowResponseMs = 3000,
        CertificateExpiryWarningDays = 14
    };

    private static HttpCheckResult Result(HttpCheckStatus status, int responseMs = 100, int? certDays = null) => new()
    {
        TargetId = Guid.NewGuid(),
        TargetName = "Müşteri sitesi",
        GroupName = "Acme",
        Url = "https://ornek.com",
        CheckedAt = DateTimeOffset.UtcNow,
        Status = status,
        StatusCode = status == HttpCheckStatus.Down ? null : 200,
        ResponseTimeMs = responseMs,
        CertificateDaysRemaining = certDays,
        Error = status == HttpCheckStatus.Down ? "Bağlantı reddedildi." : null
    };

    [Fact]
    public void DownEndpointIsCritical()
    {
        var down = Assert.Single(
            HttpAlertRules.Evaluate(Target(), Result(HttpCheckStatus.Down)),
            c => c.RuleId == HttpAlertRules.Down);

        Assert.True(down.IsBreached);
        Assert.Equal(Severity.Critical, down.Severity);
        Assert.Contains("yanıt vermiyor", down.Message);
    }

    [Fact]
    public void SlowRuleIsNotEvaluatedWhileDown()
    {
        var candidates = HttpAlertRules.Evaluate(Target(), Result(HttpCheckStatus.Down, responseMs: 5000));

        // A timeout is not "slow" — reporting it as a response time would be a made-up number.
        Assert.DoesNotContain(candidates, c => c.RuleId == HttpAlertRules.Slow);
    }

    [Fact]
    public void SlowResponseBreachesButStaysWarning()
    {
        var slow = Assert.Single(
            HttpAlertRules.Evaluate(Target(), Result(HttpCheckStatus.Degraded, responseMs: 3500)),
            c => c.RuleId == HttpAlertRules.Slow);

        Assert.True(slow.IsBreached);
        Assert.Equal(Severity.Warning, slow.Severity);
    }

    [Fact]
    public void VerySlowResponseEscalatesToCritical()
    {
        var slow = Assert.Single(
            HttpAlertRules.Evaluate(Target(), Result(HttpCheckStatus.Degraded, responseMs: 9500)),
            c => c.RuleId == HttpAlertRules.Slow);

        Assert.Equal(Severity.Critical, slow.Severity);
    }

    [Fact]
    public void CertificateRuleIsSkippedWhenNoCertificateWasRead()
    {
        var candidates = HttpAlertRules.Evaluate(Target(), Result(HttpCheckStatus.Up, certDays: null));

        // Plain HTTP, or a failed handshake: unknown is not the same as fine.
        Assert.DoesNotContain(candidates, c => c.RuleId == HttpAlertRules.Certificate);
    }

    [Fact]
    public void CertificateNearingExpiryWarns()
    {
        var cert = Assert.Single(
            HttpAlertRules.Evaluate(Target(), Result(HttpCheckStatus.Up, certDays: 10)),
            c => c.RuleId == HttpAlertRules.Certificate);

        Assert.True(cert.IsBreached);
        Assert.Equal(Severity.Warning, cert.Severity);
        Assert.Contains("10 gün", cert.Message);
    }

    [Fact]
    public void ExpiredCertificateIsCriticalAndSaysHowLongAgo()
    {
        var cert = Assert.Single(
            HttpAlertRules.Evaluate(Target(), Result(HttpCheckStatus.Up, certDays: -3)),
            c => c.RuleId == HttpAlertRules.Certificate);

        Assert.Equal(Severity.Critical, cert.Severity);
        Assert.Contains("3 gün önce doldu", cert.Message);
    }

    [Fact]
    public void HealthyEndpointReportsEveryRuleAsNotBreached()
    {
        var candidates = HttpAlertRules.Evaluate(Target(), Result(HttpCheckStatus.Up, responseMs: 120, certDays: 200));

        Assert.All(candidates, c => Assert.False(c.IsBreached));
        Assert.Equal(3, candidates.Count);
    }

    [Fact]
    public void CertificateAlertDoesNotNagEveryFifteenMinutes()
    {
        var cert = Assert.Single(
            HttpAlertRules.Evaluate(Target(), Result(HttpCheckStatus.Up, certDays: 5)),
            c => c.RuleId == HttpAlertRules.Certificate);

        // Expiry is a date, not a fluctuation; twelve hours is soon enough to hear it again.
        Assert.True(cert.RenotifyMinutes >= 720);
        Assert.Equal(1, cert.RequiredConsecutiveBreaches);
    }

    [Fact]
    public void DisablingARuleRemovesItEntirely()
    {
        var target = Target();
        target.SlowResponseMs = null;
        target.CertificateExpiryWarningDays = null;

        var candidates = HttpAlertRules.Evaluate(target, Result(HttpCheckStatus.Up, responseMs: 99999, certDays: 1));

        Assert.Single(candidates);
        Assert.Equal(HttpAlertRules.Down, candidates[0].RuleId);
    }
}
