using MssqlRealtime.Core.Common;

namespace MssqlRealtime.Core.Agents;

/// <summary>Which agents are connected right now. In-memory: connection state is not history.</summary>
public interface IAgentRegistry
{
    void MarkConnected(Guid agentId, string connectionId);
    void MarkDisconnected(string connectionId);

    bool IsConnected(Guid agentId);
    string? GetConnectionId(Guid agentId);
    IReadOnlyCollection<Guid> ConnectedAgents { get; }

    /// <summary>Resolves an agent from its SignalR connection, for messages sent after registration.</summary>
    Guid? ResolveAgent(string connectionId);
}

/// <summary>
/// Receives measurements pushed by an agent. Implemented by whichever module owns that kind
/// of measurement, so the hub stays free of module knowledge.
/// </summary>
public interface IAgentSnapshotSink
{
    /// <summary>Payload kind this sink handles, e.g. "mssql.snapshot".</summary>
    string PayloadKind { get; }

    /// <summary>
    /// Handles one measurement from an agent. The agent is trusted to have measured it, but
    /// not to decide what it means — alert evaluation stays on the hub.
    /// </summary>
    Task<Result> IngestAsync(Guid agentId, string payloadJson, CancellationToken ct = default);
}

/// <summary>Builds the work list for an agent and answers who owns which target.</summary>
public interface IAgentConfigurationProvider
{
    /// <summary>Ordering matters only for readability; the agent treats it as a set.</summary>
    Task<AgentConfiguration> GetConfigurationAsync(Guid agentId, CancellationToken ct = default);
}
