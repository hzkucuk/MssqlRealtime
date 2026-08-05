using MssqlRealtime.Api.Realtime;
using MssqlRealtime.Infrastructure.Persistence;

namespace MssqlRealtime.Tests;

public class AgentKeyTests
{
    [Fact]
    public void GeneratedKeysAreUniqueAndUrlSafe()
    {
        var keys = Enumerable.Range(0, 200).Select(_ => AgentRecord.GenerateKey()).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());

        // The key gets pasted into a config file over RDP; +, / and = make that fragile.
        Assert.All(keys, k => Assert.DoesNotContain('+', k));
        Assert.All(keys, k => Assert.DoesNotContain('/', k));
        Assert.All(keys, k => Assert.DoesNotContain('=', k));
        Assert.All(keys, k => Assert.True(k.Length >= 40));
    }

    [Fact]
    public void HashIsStableAndDiffersPerKey()
    {
        var key = AgentRecord.GenerateKey();

        Assert.Equal(AgentRecord.Hash(key), AgentRecord.Hash(key));
        Assert.NotEqual(AgentRecord.Hash(key), AgentRecord.Hash(AgentRecord.GenerateKey()));
    }

    [Fact]
    public void HashDoesNotContainTheKey()
    {
        var key = AgentRecord.GenerateKey();

        // Stored hashed on purpose: an operator who loses the key issues a new one.
        Assert.DoesNotContain(key, AgentRecord.Hash(key));
    }
}

public class AgentRegistryTests
{
    [Fact]
    public void ConnectingRegistersBothDirections()
    {
        var registry = new AgentRegistry();
        var agentId = Guid.NewGuid();

        registry.MarkConnected(agentId, "conn-1");

        Assert.True(registry.IsConnected(agentId));
        Assert.Equal("conn-1", registry.GetConnectionId(agentId));
        Assert.Equal(agentId, registry.ResolveAgent("conn-1"));
    }

    [Fact]
    public void ReconnectingReplacesTheStaleConnection()
    {
        var registry = new AgentRegistry();
        var agentId = Guid.NewGuid();

        registry.MarkConnected(agentId, "conn-1");
        registry.MarkConnected(agentId, "conn-2");

        Assert.Equal("conn-2", registry.GetConnectionId(agentId));

        // A message arriving late on the old connection must not be attributed to the agent.
        Assert.Null(registry.ResolveAgent("conn-1"));
    }

    [Fact]
    public void DisconnectingTheOldConnectionDoesNotDropTheNewOne()
    {
        var registry = new AgentRegistry();
        var agentId = Guid.NewGuid();

        registry.MarkConnected(agentId, "conn-1");
        registry.MarkConnected(agentId, "conn-2");

        // SignalR delivers OnDisconnected for the old socket after the new one is up; that
        // must not mark a live agent as gone.
        registry.MarkDisconnected("conn-1");

        Assert.True(registry.IsConnected(agentId));
        Assert.Equal("conn-2", registry.GetConnectionId(agentId));
    }

    [Fact]
    public void DisconnectingRemovesTheAgent()
    {
        var registry = new AgentRegistry();
        var agentId = Guid.NewGuid();

        registry.MarkConnected(agentId, "conn-1");
        registry.MarkDisconnected("conn-1");

        Assert.False(registry.IsConnected(agentId));
        Assert.Null(registry.ResolveAgent("conn-1"));
        Assert.Empty(registry.ConnectedAgents);
    }

    [Fact]
    public void UnknownConnectionResolvesToNothing()
    {
        var registry = new AgentRegistry();

        // An unregistered socket can push no measurements — this is what enforces it.
        Assert.Null(registry.ResolveAgent("never-seen"));
    }

    [Fact]
    public void SeveralAgentsAreTrackedIndependently()
    {
        var registry = new AgentRegistry();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        registry.MarkConnected(first, "conn-1");
        registry.MarkConnected(second, "conn-2");
        registry.MarkDisconnected("conn-1");

        Assert.False(registry.IsConnected(first));
        Assert.True(registry.IsConnected(second));
        Assert.Single(registry.ConnectedAgents);
    }
}
