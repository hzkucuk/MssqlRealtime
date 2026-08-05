using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Modules.Http.Models;

namespace MssqlRealtime.Modules.Http;

/// <summary>
/// Threshold rules for an endpoint. Same contract as the MSSQL module's: report every rule
/// every cycle so the engine can clear what recovered, and never report a rule you could not
/// measure.
/// </summary>
public static class HttpAlertRules
{
    public const string Down = "down";
    public const string Slow = "slow";
    public const string Certificate = "certificate";

    public static IReadOnlyList<AlertCandidate> Evaluate(HttpTarget target, HttpCheckResult result)
    {
        var candidates = new List<AlertCandidate>();

        if (target.AlertOnDown)
        {
            var isDown = result.Status == HttpCheckStatus.Down;

            candidates.Add(new AlertCandidate
            {
                RuleId = Down,
                RuleTitle = "Erişilemiyor",
                IsBreached = isDown,
                Severity = Severity.Critical,
                Message = isDown
                    ? $"{target.Url} yanıt vermiyor — {result.Error ?? "bilinmeyen hata"}"
                    : string.Empty,
                RequiredConsecutiveBreaches = target.AlertConsecutiveBreaches,
                RenotifyMinutes = target.AlertRenotifyMinutes
            });
        }

        // Down endpoints have no meaningful response time, and a timeout is not "slow".
        if (target.SlowResponseMs is { } slowLimit && result.Status != HttpCheckStatus.Down)
        {
            candidates.Add(new AlertCandidate
            {
                RuleId = Slow,
                RuleTitle = "Yavaş yanıt",
                IsBreached = result.ResponseTimeMs >= slowLimit,
                Severity = result.ResponseTimeMs >= slowLimit * 3 ? Severity.Critical : Severity.Warning,
                Message = $"Yanıt {result.ResponseTimeMs} ms — sınır {slowLimit} ms",
                Value = result.ResponseTimeMs,
                Threshold = slowLimit,
                Unit = "ms",
                RequiredConsecutiveBreaches = target.AlertConsecutiveBreaches,
                RenotifyMinutes = target.AlertRenotifyMinutes
            });
        }

        // Only when we actually read a certificate: absent is not the same as fine.
        if (target.CertificateExpiryWarningDays is { } warnDays && result.CertificateDaysRemaining is { } daysLeft)
        {
            var expired = daysLeft <= 0;

            candidates.Add(new AlertCandidate
            {
                RuleId = Certificate,
                RuleTitle = "TLS sertifikası",
                IsBreached = daysLeft <= warnDays,
                Severity = expired || daysLeft <= 3 ? Severity.Critical : Severity.Warning,
                Message = expired
                    ? $"Sertifikanın süresi {Math.Abs(daysLeft)} gün önce doldu."
                    : $"Sertifikanın bitmesine {daysLeft} gün kaldı — uyarı sınırı {warnDays} gün.",
                Value = daysLeft,
                Threshold = warnDays,
                Unit = " gün",
                // Certificate expiry is a fact, not a fluctuation: no need to see it repeatedly.
                RequiredConsecutiveBreaches = 1,
                // And it does not need re-announcing every quarter hour.
                RenotifyMinutes = Math.Max(target.AlertRenotifyMinutes, 720)
            });
        }

        return candidates;
    }
}
