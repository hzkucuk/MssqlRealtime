using MssqlRealtime.Core.Alerts;

namespace MssqlRealtime.Core.Agents;

/// <summary>State of one agent as the health check sees it.</summary>
public sealed record AgentHealthInput
{
    public required Guid AgentId { get; init; }
    public required string Name { get; init; }
    public string? MachineName { get; init; }

    /// <summary>Live hub connection right now.</summary>
    public required bool IsConnected { get; init; }

    /// <summary>Null when the agent has never connected — a pending install, not an outage.</summary>
    public DateTimeOffset? FirstConnectedUtc { get; init; }

    public DateTimeOffset? LastSeenUtc { get; init; }

    /// <summary>Enabled servers assigned to this agent.</summary>
    public required int AssignedTargets { get; init; }
}

/// <summary>
/// Decides whether an agent's silence is worth waking someone for.
/// <para>
/// Pulled out of the background service so the judgement can be tested directly: this is the
/// rule that stops a monitoring product from failing silently, and it has three easy ways to
/// be wrong — paging for installs that never happened, for agents with nothing to do, or not
/// at all.
/// </para>
/// </summary>
public static class AgentHealthEvaluator
{
    public const string RuleId = "agent-silent";
    public const string RuleTitle = "Agent sessiz";

    /// <summary>Returns null when the agent should not be evaluated at all.</summary>
    public static AlertCandidate? Evaluate(
        AgentHealthInput agent,
        TimeSpan offlineAfter,
        DateTimeOffset nowUtc,
        int renotifyMinutes = 60)
    {
        // Never connected: someone issued a key and has not finished the install. Alerting
        // here would page an operator for every agent they are still preparing.
        if (agent.FirstConnectedUtc is null)
        {
            return null;
        }

        // Nothing assigned: its silence costs nothing, so it is not an incident.
        if (agent.AssignedTargets == 0)
        {
            return null;
        }

        var silentFor = agent.LastSeenUtc is { } seen ? nowUtc - seen : TimeSpan.MaxValue;
        var isSilent = !agent.IsConnected && silentFor > offlineAfter;

        return new AlertCandidate
        {
            RuleId = RuleId,
            RuleTitle = RuleTitle,
            IsBreached = isSilent,
            Severity = Severity.Critical,
            Message = isSilent
                ? $"{agent.Name} {Describe(silentFor)} sessiz — bu agent'a bağlı "
                  + $"{agent.AssignedTargets} sunucu artık izlenmiyor."
                : string.Empty,
            Value = silentFor == TimeSpan.MaxValue ? null : Math.Round(silentFor.TotalMinutes, 1),
            Threshold = Math.Round(offlineAfter.TotalMinutes, 1),
            Unit = " dk",
            // The offline window is itself the grace period; waiting longer only delays news
            // that servers are already unmonitored.
            RequiredConsecutiveBreaches = 1,
            RenotifyMinutes = renotifyMinutes
        };
    }

    private static string Describe(TimeSpan span) => span switch
    {
        { TotalMinutes: < 60 } => $"{span.TotalMinutes:0} dakikadır",
        { TotalHours: < 24 } => $"{span.TotalHours:0} saattir",
        _ => $"{span.TotalDays:0} gündür"
    };
}
