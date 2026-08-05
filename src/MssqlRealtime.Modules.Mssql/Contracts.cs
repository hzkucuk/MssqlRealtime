using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql;

/// <summary>
/// A server profile as it leaves the API. The password is never in here — only whether one
/// is stored, so the phone can show "kayıtlı" without ever holding the secret.
/// </summary>
public sealed record ServerProfileDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string CustomerName { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string InitialCatalog { get; init; }
    public required SqlAuthMode AuthMode { get; init; }
    public string? Username { get; init; }
    public required bool HasPassword { get; init; }
    public required bool EncryptConnection { get; init; }
    public required bool TrustServerCertificate { get; init; }
    public required int ConnectTimeoutSeconds { get; init; }
    public required int CommandTimeoutSeconds { get; init; }
    public required bool Enabled { get; init; }
    public required int PollIntervalSeconds { get; init; }


    public required AlertThresholdsDto Thresholds { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public static ServerProfileDto From(ServerProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        CustomerName = p.CustomerName,
        Host = p.Host,
        Port = p.Port,
        InitialCatalog = p.InitialCatalog,
        AuthMode = p.AuthMode,
        Username = p.Username,
        HasPassword = !string.IsNullOrEmpty(p.ProtectedPassword),
        EncryptConnection = p.EncryptConnection,
        TrustServerCertificate = p.TrustServerCertificate,
        ConnectTimeoutSeconds = p.ConnectTimeoutSeconds,
        CommandTimeoutSeconds = p.CommandTimeoutSeconds,
        Enabled = p.Enabled,
        PollIntervalSeconds = p.PollIntervalSeconds,
        Thresholds = AlertThresholdsDto.From(p),
        UpdatedAt = p.UpdatedAt
    };
}

/// <summary>The user's own limits. Null means "do not alert on this".</summary>
public sealed record AlertThresholdsDto
{
    public int? CpuPercent { get; init; }
    public int? MemoryPercent { get; init; }
    public int? SqlProcessMemoryMb { get; init; }
    public int? BlockedSessions { get; init; }
    public int? LongRunningQuerySeconds { get; init; }
    public int? SessionCount { get; init; }
    public int ConsecutiveBreaches { get; init; } = 3;
    public int RenotifyMinutes { get; init; } = 15;
    public bool AlertOnOffline { get; init; } = true;

    public static AlertThresholdsDto From(ServerProfile p) => new()
    {
        CpuPercent = p.CpuAlertPercent,
        MemoryPercent = p.MemoryAlertPercent,
        SqlProcessMemoryMb = p.SqlProcessMemoryAlertMb,
        BlockedSessions = p.BlockedSessionAlertThreshold,
        LongRunningQuerySeconds = p.LongRunningQuerySecondsThreshold,
        SessionCount = p.SessionCountAlertThreshold,
        ConsecutiveBreaches = p.AlertConsecutiveBreaches,
        RenotifyMinutes = p.AlertRenotifyMinutes,
        AlertOnOffline = p.AlertOnOffline
    };

    public void ApplyTo(ServerProfile p)
    {
        p.CpuAlertPercent = CpuPercent;
        p.MemoryAlertPercent = MemoryPercent;
        p.SqlProcessMemoryAlertMb = SqlProcessMemoryMb;
        p.BlockedSessionAlertThreshold = BlockedSessions;
        p.LongRunningQuerySecondsThreshold = LongRunningQuerySeconds;
        p.SessionCountAlertThreshold = SessionCount;
        p.AlertConsecutiveBreaches = Math.Clamp(ConsecutiveBreaches, 1, 60);
        p.AlertRenotifyMinutes = Math.Clamp(RenotifyMinutes, 1, 1440);
        p.AlertOnOffline = AlertOnOffline;
    }
}

/// <summary>Create/update payload. An omitted password keeps the stored one.</summary>
public sealed record ServerProfileRequest
{
    public string Name { get; init; } = string.Empty;
    public string CustomerName { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 1433;
    public string InitialCatalog { get; init; } = "master";
    public SqlAuthMode AuthMode { get; init; } = SqlAuthMode.SqlLogin;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool EncryptConnection { get; init; } = true;
    public bool TrustServerCertificate { get; init; } = true;
    public int ConnectTimeoutSeconds { get; init; } = 5;
    public int CommandTimeoutSeconds { get; init; } = 15;
    public bool Enabled { get; init; } = true;
    public int PollIntervalSeconds { get; init; } = 5;


    public AlertThresholdsDto? Thresholds { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name)) errors.Add("Ad zorunlu.");
        if (string.IsNullOrWhiteSpace(CustomerName)) errors.Add("Müşteri adı zorunlu.");
        if (string.IsNullOrWhiteSpace(Host)) errors.Add("Sunucu adresi zorunlu.");
        if (Port is < 1 or > 65535) errors.Add("Port 1-65535 aralığında olmalı.");
        if (PollIntervalSeconds is < 1 or > 3600) errors.Add("Sorgulama aralığı 1-3600 sn olmalı.");
        if (ConnectTimeoutSeconds is < 1 or > 120) errors.Add("Bağlantı zaman aşımı 1-120 sn olmalı.");
        if (CommandTimeoutSeconds is < 1 or > 300) errors.Add("Komut zaman aşımı 1-300 sn olmalı.");

        if (AuthMode == SqlAuthMode.SqlLogin && string.IsNullOrWhiteSpace(Username))
        {
            errors.Add("SQL girişi için kullanıcı adı zorunlu.");
        }

        return errors;
    }
}

public sealed record KillSessionRequest(int SessionId);
