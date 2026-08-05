using Microsoft.AspNetCore.SignalR;
using MssqlRealtime.Core.Agents;

namespace MssqlRealtime.Api.Realtime;

public sealed class AgentNotifier(
    IHubContext<AgentHub> hub,
    IAgentRegistry registry,
    IServiceScopeFactory scopeFactory,
    ILogger<AgentNotifier> logger) : IAgentNotifier
{
    public async Task NotifyConfigurationChangedAsync(Guid agentId, CancellationToken ct = default)
    {
        if (registry.GetConnectionId(agentId) is not { } connectionId)
        {
            // Offline agents pick the change up when they reconnect and register.
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IAgentConfigurationProvider>();
            var configuration = await provider.GetConfigurationAsync(agentId, ct);

            await hub.Clients.Client(connectionId)
                .SendAsync(AgentProtocol.ConfigurationChanged, configuration, ct);

            logger.LogInformation(
                "Pushed configuration to agent {AgentId}: {Count} target(s)",
                agentId, configuration.SqlTargets.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not push configuration to agent {AgentId}", agentId);
        }
    }

    public async Task NotifyConfigurationChangedAsync(IEnumerable<Guid> agentIds, CancellationToken ct = default)
    {
        foreach (var agentId in agentIds.Distinct())
        {
            await NotifyConfigurationChangedAsync(agentId, ct);
        }
    }
}
