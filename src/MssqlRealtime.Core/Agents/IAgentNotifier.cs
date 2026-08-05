namespace MssqlRealtime.Core.Agents;

/// <summary>
/// Pushes a new work list to a connected agent.
/// <para>
/// Without this an agent only learns about a newly assigned server when it next reconnects,
/// which could be days. Assigning a server from the phone has to take effect now.
/// </para>
/// </summary>
public interface IAgentNotifier
{
    /// <summary>Sends the current configuration to one agent, if it is connected.</summary>
    Task NotifyConfigurationChangedAsync(Guid agentId, CancellationToken ct = default);

    /// <summary>Used when a change could affect several agents (e.g. a server was reassigned).</summary>
    Task NotifyConfigurationChangedAsync(IEnumerable<Guid> agentIds, CancellationToken ct = default);
}
