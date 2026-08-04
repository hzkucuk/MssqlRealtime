using System.Collections.Concurrent;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Polling;

public sealed class SnapshotCache : ISnapshotCache
{
    private readonly ConcurrentDictionary<Guid, ServerSnapshot> _snapshots = new();

    public ServerSnapshot? Get(Guid serverId) =>
        _snapshots.TryGetValue(serverId, out var snapshot) ? snapshot : null;

    public IReadOnlyList<ServerSnapshot> GetAll() =>
        _snapshots.Values.OrderBy(s => s.CustomerName).ThenBy(s => s.ServerName).ToList();

    public void Set(ServerSnapshot snapshot) => _snapshots[snapshot.ServerId] = snapshot;

    public void Remove(Guid serverId) => _snapshots.TryRemove(serverId, out _);
}
