using MssqlRealtime.Core.Common;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql;

public interface IServerProfileStore
{
    Task<IReadOnlyList<ServerProfile>> GetAllAsync(CancellationToken ct = default);
    Task<ServerProfile?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ServerProfile> AddAsync(ServerProfile profile, CancellationToken ct = default);
    Task<Result> UpdateAsync(ServerProfile profile, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Assembles connection strings; the only place a decrypted password exists.</summary>
public interface IConnectionStringFactory
{
    Result<string> Build(ServerProfile profile, string? applicationName = null);
}

/// <summary>
/// Last snapshot per server, so a phone that just opened the app sees the current state
/// immediately instead of waiting a full poll interval for the first push.
/// </summary>
public interface ISnapshotCache
{
    ServerSnapshot? Get(Guid serverId);
    IReadOnlyList<ServerSnapshot> GetAll();
    void Set(ServerSnapshot snapshot);
    void Remove(Guid serverId);
}

/// <summary>Write actions against a monitored instance. Deliberately narrow.</summary>
public interface IServerActions
{
    Task<Result<ServerSnapshot>> TestConnectionAsync(ServerProfile profile, CancellationToken ct = default);
    Task<Result> KillSessionAsync(Guid serverId, int sessionId, CancellationToken ct = default);
}
