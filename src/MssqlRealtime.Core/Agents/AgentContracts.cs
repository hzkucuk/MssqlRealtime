namespace MssqlRealtime.Core.Agents;

/// <summary>
/// Shared vocabulary between the hub and the agents that connect to it.
/// <para>
/// The agent exists for one reason: most customer databases sit behind NAT or a firewall that
/// will never allow an inbound connection from us. So the direction is reversed — a small
/// service on the customer's side dials out to the hub, measures locally, and pushes results
/// up. Nothing has to be opened on their perimeter.
/// </para>
/// </summary>
public static class AgentProtocol
{
    public const string HubPath = "/hubs/agent";

    /// <summary>Bumped when the message shape changes in a way an old agent cannot handle.</summary>
    public const int ProtocolVersion = 1;

    // --- Methods the agent invokes on the hub ---
    public const string Register = nameof(Register);
    public const string Heartbeat = nameof(Heartbeat);
    public const string PublishSnapshot = nameof(PublishSnapshot);

    // --- Methods the hub invokes on the agent ---
    public const string ConfigurationChanged = nameof(ConfigurationChanged);
}

/// <summary>What an agent tells the hub about itself when it connects.</summary>
public sealed record AgentRegistration
{
    /// <summary>Enrollment key, issued in the UI. This is the agent's only credential.</summary>
    public required string EnrollmentKey { get; init; }

    /// <summary>Machine name, so an operator can recognise it in the list.</summary>
    public required string MachineName { get; init; }

    public required string Version { get; init; }
    public int ProtocolVersion { get; init; } = AgentProtocol.ProtocolVersion;
    public string? OperatingSystem { get; init; }
}

public sealed record AgentRegistrationResult
{
    public required bool Accepted { get; init; }
    public string? Error { get; init; }
    public Guid AgentId { get; init; }
    public string? AgentName { get; init; }

    /// <summary>What this agent should be monitoring. Sent on connect and on every change.</summary>
    public AgentConfiguration Configuration { get; init; } = new();
}

/// <summary>
/// The work assigned to one agent. Deliberately a plain data contract: the agent runs the same
/// probe code as the hub, so only the targets travel — never the logic.
/// </summary>
public sealed record AgentConfiguration
{
    public IReadOnlyList<AgentSqlTarget> SqlTargets { get; init; } = [];

    /// <summary>Version marker so the agent can ignore a configuration it already has.</summary>
    public string Revision { get; init; } = string.Empty;
}

/// <summary>
/// One SQL Server the agent should poll. The password travels over the (TLS) hub connection
/// and is held only in the agent's memory — it is never written to disk on the customer side.
/// </summary>
public sealed record AgentSqlTarget
{
    public required Guid ServerId { get; init; }
    public required string Name { get; init; }
    public required string CustomerName { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string InitialCatalog { get; init; }
    public required bool IntegratedSecurity { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public required bool EncryptConnection { get; init; }
    public required bool TrustServerCertificate { get; init; }
    public required int ConnectTimeoutSeconds { get; init; }
    public required int CommandTimeoutSeconds { get; init; }
    public required int PollIntervalSeconds { get; init; }
}

/// <summary>Periodic liveness signal; also how the hub notices an agent has gone quiet.</summary>
public sealed record AgentHeartbeat
{
    public required Guid AgentId { get; init; }
    public required DateTimeOffset SentAtUtc { get; init; }

    /// <summary>Targets the agent believes it is currently polling.</summary>
    public int ActiveTargets { get; init; }

    /// <summary>Non-fatal problems worth surfacing in the UI, e.g. a target it cannot reach.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>An agent as the operator sees it.</summary>
public sealed record AgentInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? MachineName { get; init; }
    public string? Version { get; init; }
    public string? OperatingSystem { get; init; }
    public required bool IsConnected { get; init; }
    public DateTimeOffset? LastSeenUtc { get; init; }
    public DateTimeOffset? RegisteredAtUtc { get; init; }
    public int AssignedTargets { get; init; }

    /// <summary>True once the key has been used; a never-connected agent is still pending.</summary>
    public bool HasEverConnected => LastSeenUtc is not null;
}
