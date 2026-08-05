using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MssqlRealtime.Core.Agents;
using MssqlRealtime.Infrastructure.Persistence;

namespace MssqlRealtime.Api.Realtime;

/// <summary>
/// Where agents connect. Separate from <see cref="ToolsHub"/> on purpose: agents authenticate
/// with an enrollment key rather than an operator login, and they push measurements instead
/// of consuming them.
/// </summary>
public sealed class AgentHub(
    AppDbContext db,
    IAgentRegistry registry,
    IEnumerable<IAgentSnapshotSink> sinks,
    IAgentConfigurationProvider configuration,
    ILogger<AgentHub> logger) : Hub
{
    private readonly IAgentSnapshotSink[] _sinks = sinks.ToArray();

    /// <summary>
    /// First call after connecting. Until this succeeds the connection can do nothing else —
    /// an unregistered connection is just an open socket.
    /// </summary>
    public async Task<AgentRegistrationResult> Register(AgentRegistration registration)
    {
        if (registration.ProtocolVersion != AgentProtocol.ProtocolVersion)
        {
            return new AgentRegistrationResult
            {
                Accepted = false,
                Error = $"Agent protokol sürümü uyumsuz (agent {registration.ProtocolVersion}, "
                      + $"sunucu {AgentProtocol.ProtocolVersion}). Agent'ı güncelleyin."
            };
        }

        var hash = AgentRecord.Hash(registration.EnrollmentKey ?? string.Empty);
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.KeyHash == hash);

        if (agent is null)
        {
            // Deliberately vague: a probing client should not learn whether a key merely
            // expired or never existed.
            logger.LogWarning(
                "Agent registration rejected from {Machine} ({Address})",
                registration.MachineName,
                Context.GetHttpContext()?.Connection.RemoteIpAddress);

            return new AgentRegistrationResult { Accepted = false, Error = "Kayıt anahtarı geçersiz." };
        }

        if (!agent.Enabled)
        {
            return new AgentRegistrationResult { Accepted = false, Error = "Bu agent devre dışı bırakılmış." };
        }

        var now = DateTime.UtcNow;
        agent.MachineName = registration.MachineName;
        agent.Version = registration.Version;
        agent.OperatingSystem = registration.OperatingSystem;
        agent.LastSeenUtc = now;
        agent.FirstConnectedUtc ??= now;
        await db.SaveChangesAsync();

        registry.MarkConnected(agent.Id, Context.ConnectionId);

        logger.LogInformation(
            "Agent {Name} connected from {Machine} (v{Version})",
            agent.Name, registration.MachineName, registration.Version);

        return new AgentRegistrationResult
        {
            Accepted = true,
            AgentId = agent.Id,
            AgentName = agent.Name,
            Configuration = await configuration.GetConfigurationAsync(agent.Id)
        };
    }

    public async Task Heartbeat(AgentHeartbeat heartbeat)
    {
        if (registry.ResolveAgent(Context.ConnectionId) is not { } agentId)
        {
            return;
        }

        await db.Agents
            .Where(a => a.Id == agentId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastSeenUtc, DateTime.UtcNow));

        foreach (var warning in heartbeat.Warnings)
        {
            logger.LogWarning("Agent {AgentId} warning: {Warning}", agentId, warning);
        }
    }

    /// <summary>
    /// A measurement from the customer's side. The agent measured it; the hub decides what it
    /// means — alert thresholds, notification routing and history all stay here.
    /// </summary>
    public async Task PublishSnapshot(string payloadKind, string payloadJson)
    {
        if (registry.ResolveAgent(Context.ConnectionId) is not { } agentId)
        {
            logger.LogWarning("Snapshot from an unregistered connection was ignored");
            return;
        }

        var sink = _sinks.FirstOrDefault(s =>
            string.Equals(s.PayloadKind, payloadKind, StringComparison.OrdinalIgnoreCase));

        if (sink is null)
        {
            logger.LogWarning("No sink for agent payload kind {Kind}", payloadKind);
            return;
        }

        var result = await sink.IngestAsync(agentId, payloadJson);
        if (result.IsFailure)
        {
            logger.LogWarning("Agent payload rejected: {Error}", result.Error);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (registry.ResolveAgent(Context.ConnectionId) is { } agentId)
        {
            logger.LogInformation("Agent {AgentId} disconnected", agentId);
        }

        registry.MarkDisconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public sealed class AgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<Guid, string> _byAgent = new();
    private readonly ConcurrentDictionary<string, Guid> _byConnection = new(StringComparer.Ordinal);

    public void MarkConnected(Guid agentId, string connectionId)
    {
        // A reconnecting agent replaces its own previous entry; the stale connection id is
        // dropped so a message arriving on it after the fact is not attributed to the agent.
        if (_byAgent.TryGetValue(agentId, out var previous) && previous != connectionId)
        {
            _byConnection.TryRemove(previous, out _);
        }

        _byAgent[agentId] = connectionId;
        _byConnection[connectionId] = agentId;
    }

    public void MarkDisconnected(string connectionId)
    {
        if (_byConnection.TryRemove(connectionId, out var agentId)
            && _byAgent.TryGetValue(agentId, out var current)
            && current == connectionId)
        {
            _byAgent.TryRemove(agentId, out _);
        }
    }

    public bool IsConnected(Guid agentId) => _byAgent.ContainsKey(agentId);

    public string? GetConnectionId(Guid agentId) => _byAgent.TryGetValue(agentId, out var id) ? id : null;

    public IReadOnlyCollection<Guid> ConnectedAgents => _byAgent.Keys.ToList();

    public Guid? ResolveAgent(string connectionId) =>
        _byConnection.TryGetValue(connectionId, out var agentId) ? agentId : null;
}
