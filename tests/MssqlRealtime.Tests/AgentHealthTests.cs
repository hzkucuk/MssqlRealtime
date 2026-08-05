using MssqlRealtime.Core.Agents;
using MssqlRealtime.Core.Alerts;

namespace MssqlRealtime.Tests;

/// <summary>
/// The rule that stops the product from failing silently. It has three easy ways to be wrong:
/// paging for installs nobody finished, paging for agents with nothing to do, or staying
/// quiet while servers go unmonitored.
/// </summary>
public class AgentHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 14, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(3);

    private static AgentHealthInput Agent(
        bool connected = false,
        double? silentMinutes = 10,
        int assigned = 2,
        bool everConnected = true) => new()
    {
        AgentId = Guid.NewGuid(),
        Name = "Acme — SQL sunucusu",
        MachineName = "ACME-SQL01",
        IsConnected = connected,
        FirstConnectedUtc = everConnected ? Now.AddDays(-5) : null,
        LastSeenUtc = silentMinutes is null ? null : Now.AddMinutes(-silentMinutes.Value),
        AssignedTargets = assigned
    };

    [Fact]
    public void SilentAgentWithAssignedServersIsCritical()
    {
        var candidate = AgentHealthEvaluator.Evaluate(Agent(), OfflineAfter, Now);

        Assert.NotNull(candidate);
        Assert.True(candidate.IsBreached);
        Assert.Equal(Severity.Critical, candidate.Severity);

        // The message has to say what the silence costs, not just that it happened.
        Assert.Contains("2 sunucu", candidate.Message);
        Assert.Contains("izlenmiyor", candidate.Message);
    }

    [Fact]
    public void ConnectedAgentIsNotBreached()
    {
        var candidate = AgentHealthEvaluator.Evaluate(Agent(connected: true, silentMinutes: 0), OfflineAfter, Now);

        Assert.NotNull(candidate);
        Assert.False(candidate.IsBreached);
    }

    [Fact]
    public void SilenceInsideTheWindowIsNotAnOutage()
    {
        // Two minutes quiet with a three-minute window: a reconnect in progress, not an alert.
        var candidate = AgentHealthEvaluator.Evaluate(Agent(silentMinutes: 2), OfflineAfter, Now);

        Assert.NotNull(candidate);
        Assert.False(candidate.IsBreached);
    }

    [Fact]
    public void NeverConnectedAgentIsNotEvaluated()
    {
        // A key was issued and the install is not finished; paging for that is noise.
        Assert.Null(AgentHealthEvaluator.Evaluate(Agent(everConnected: false, silentMinutes: null), OfflineAfter, Now));
    }

    [Fact]
    public void AgentWithNoAssignedServersIsNotEvaluated()
    {
        // Its silence costs nothing.
        Assert.Null(AgentHealthEvaluator.Evaluate(Agent(assigned: 0), OfflineAfter, Now));
    }

    [Fact]
    public void ConnectedButUnassignedIsStillNotEvaluated()
    {
        Assert.Null(AgentHealthEvaluator.Evaluate(Agent(connected: true, assigned: 0), OfflineAfter, Now));
    }

    [Fact]
    public void MeasuredValueAndThresholdAreReported()
    {
        var candidate = AgentHealthEvaluator.Evaluate(Agent(silentMinutes: 7.5), OfflineAfter, Now);

        Assert.NotNull(candidate);
        Assert.Equal(7.5, candidate.Value);
        Assert.Equal(3, candidate.Threshold);
    }

    [Fact]
    public void FiresOnTheFirstCheck()
    {
        var candidate = AgentHealthEvaluator.Evaluate(Agent(), OfflineAfter, Now);

        // The offline window is already the grace period; debouncing it again would only
        // delay news that servers are unmonitored.
        Assert.NotNull(candidate);
        Assert.Equal(1, candidate.RequiredConsecutiveBreaches);
    }

    [Theory]
    [InlineData(45, "45 dakikadır")]
    [InlineData(180, "3 saattir")]
    [InlineData(2880, "2 gündür")]
    public void DurationIsReadableAtEveryScale(double silentMinutes, string expected)
    {
        var candidate = AgentHealthEvaluator.Evaluate(Agent(silentMinutes: silentMinutes), OfflineAfter, Now);

        Assert.NotNull(candidate);
        Assert.Contains(expected, candidate.Message);
    }

    [Fact]
    public void AgentSilentSinceBeforeRecordsExistIsStillBreached()
    {
        // LastSeen unknown but it has connected before: treat as silent rather than healthy.
        var candidate = AgentHealthEvaluator.Evaluate(Agent(silentMinutes: null), OfflineAfter, Now);

        Assert.NotNull(candidate);
        Assert.True(candidate.IsBreached);
        Assert.Null(candidate.Value);
    }

    [Fact]
    public void RecoveryClearsThroughTheEngine()
    {
        var engine = new AlertEngine();
        var agent = Agent();
        var target = new AlertTarget
        {
            ModuleId = "platform",
            TargetId = agent.AgentId.ToString("N"),
            TargetName = agent.Name
        };

        var silent = AgentHealthEvaluator.Evaluate(agent, OfflineAfter, Now)!;
        var raised = engine.Evaluate(target, [silent], Now);
        Assert.Single(raised.ToNotify);

        var back = agent with { IsConnected = true, LastSeenUtc = Now.AddSeconds(30) };
        var recovered = AgentHealthEvaluator.Evaluate(back, OfflineAfter, Now.AddMinutes(1))!;
        var cleared = engine.Evaluate(target, [recovered], Now.AddMinutes(1));

        var notification = Assert.Single(cleared.ToNotify);
        Assert.True(notification.IsCleared);
    }
}
