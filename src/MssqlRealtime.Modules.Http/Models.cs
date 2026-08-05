using MssqlRealtime.Core.Alerts;

namespace MssqlRealtime.Modules.Http.Models;

/// <summary>One monitored endpoint: a customer's site, an API, a payment provider callback.</summary>
public sealed class HttpTarget
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public string Name { get; set; } = string.Empty;

    /// <summary>Grouping label, mirroring the MSSQL module's customer field.</summary>
    public string GroupName { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";

    /// <summary>Status code that counts as healthy. 0 means "any 2xx".</summary>
    public int ExpectedStatusCode { get; set; }

    /// <summary>Optional substring the body must contain — catches "200 OK" error pages.</summary>
    public string? ExpectedBodyContains { get; set; }

    public bool Enabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Whether to trust an invalid TLS certificate (internal hosts with self-signed certs).</summary>
    public bool IgnoreCertificateErrors { get; set; }

    // --- Thresholds -----------------------------------------------------------------------
    public bool AlertOnDown { get; set; } = true;

    /// <summary>Warn when a response takes longer than this. Null disables the rule.</summary>
    public int? SlowResponseMs { get; set; } = 3000;

    /// <summary>Warn this many days before the TLS certificate expires. Null disables.</summary>
    public int? CertificateExpiryWarningDays { get; set; } = 14;

    public int AlertConsecutiveBreaches { get; set; } = 2;
    public int AlertRenotifyMinutes { get; set; } = 15;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum HttpCheckStatus
{
    Unknown = 0,
    Up = 1,
    Down = 2,
    Degraded = 3
}

/// <summary>Result of one check; this is what is broadcast and what the phone card shows.</summary>
public sealed record HttpCheckResult
{
    public required Guid TargetId { get; init; }
    public required string TargetName { get; init; }
    public required string GroupName { get; init; }
    public required string Url { get; init; }
    public required DateTimeOffset CheckedAt { get; init; }
    public required HttpCheckStatus Status { get; init; }

    public int? StatusCode { get; init; }
    public int ResponseTimeMs { get; init; }
    public string? Error { get; init; }
    public long? ContentLength { get; init; }

    /// <summary>Days until the TLS certificate expires; null for plain HTTP or on failure.</summary>
    public int? CertificateDaysRemaining { get; init; }
    public string? CertificateSubject { get; init; }

    /// <summary>Rolling availability over the recent window kept in memory.</summary>
    public double? UptimePercent { get; init; }
    public int RecentChecks { get; init; }

    public IReadOnlyList<AlertState> ActiveAlerts { get; init; } = [];
    public Severity Severity { get; init; }
}
