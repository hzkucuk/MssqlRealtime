namespace MssqlRealtime.Modules.Mssql.Models;

public enum SqlAuthMode
{
    SqlLogin = 0,
    Integrated = 1
}

/// <summary>
/// One monitored MSSQL instance, typically one customer. The password is stored
/// encrypted at rest and never leaves the API in clear text.
/// </summary>
public sealed class ServerProfile
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Display name, e.g. "Acme Ltd - Mikro V17".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Customer/tenant grouping label, used for filtering on mobile.</summary>
    public string CustomerName { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 1433;

    /// <summary>Initial catalog. "master" is enough for every DMV we read.</summary>
    public string InitialCatalog { get; set; } = "master";

    public SqlAuthMode AuthMode { get; set; } = SqlAuthMode.SqlLogin;
    public string? Username { get; set; }

    /// <summary>Ciphertext produced by ISecretProtector. Never serialized to clients.</summary>
    public string? ProtectedPassword { get; set; }

    public bool EncryptConnection { get; set; } = true;
    public bool TrustServerCertificate { get; set; } = true;
    public int ConnectTimeoutSeconds { get; set; } = 5;
    public int CommandTimeoutSeconds { get; set; } = 15;

    public bool Enabled { get; set; } = true;


    /// <summary>How often the poller refreshes this server.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    // --- Alert thresholds: user-defined per server, evaluated on every snapshot ---

    /// <summary>Machine-wide CPU %. Null disables the rule.</summary>
    public int? CpuAlertPercent { get; set; } = 85;

    /// <summary>Machine-wide physical memory used %. Null disables the rule.</summary>
    public int? MemoryAlertPercent { get; set; } = 90;

    /// <summary>sqlservr.exe private memory in MB. Null disables the rule.</summary>
    public int? SqlProcessMemoryAlertMb { get; set; }

    public int? BlockedSessionAlertThreshold { get; set; } = 1;
    public int? LongRunningQuerySecondsThreshold { get; set; } = 30;
    public int? SessionCountAlertThreshold { get; set; } = 200;

    /// <summary>
    /// A rule must stay breached this many consecutive polls before it fires.
    /// Stops a single 5-second CPU spike from waking the phone.
    /// </summary>
    public int AlertConsecutiveBreaches { get; set; } = 3;

    /// <summary>Minimum gap between two notifications for the same still-active rule.</summary>
    public int AlertRenotifyMinutes { get; set; } = 15;

    /// <summary>Fire a notification when the server stops answering.</summary>
    public bool AlertOnOffline { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool UsesIntegratedAuth => AuthMode == SqlAuthMode.Integrated;
}
