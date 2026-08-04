using System.Data.Common;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql.Probes;

/// <summary>
/// A single unit of collection against one MSSQL instance.
/// <para>
/// This is the main extension point of the product: to monitor something new, add a probe,
/// register it in DI, and expose whatever it collected on <see cref="SnapshotBuilder"/>.
/// The poller runs every registered probe against a shared connection and one probe failing
/// never takes the snapshot down.
/// </para>
/// </summary>
public interface ISqlProbe
{
    /// <summary>Stable id, used in logs and in the per-probe error list.</summary>
    string Name { get; }

    /// <summary>Lower runs first. Probes that others depend on (sessions) take a low order.</summary>
    int Order => 100;

    /// <summary>
    /// Cost hint. Cheap probes run on every poll; expensive ones every Nth poll.
    /// </summary>
    int EveryNthPoll => 1;

    bool AppliesTo(ServerProfile profile) => true;

    Task ExecuteAsync(ProbeContext context, CancellationToken cancellationToken);
}

/// <summary>What a probe is handed: the profile, an open connection, and the builder to fill.</summary>
public sealed class ProbeContext(ServerProfile profile, DbConnection connection, SnapshotBuilder builder, long pollNumber)
{
    public ServerProfile Profile { get; } = profile;
    public DbConnection Connection { get; } = connection;
    public SnapshotBuilder Builder { get; } = builder;

    /// <summary>Monotonic counter for this server, so probes can throttle themselves.</summary>
    public long PollNumber { get; } = pollNumber;

    public int CommandTimeoutSeconds => Profile.CommandTimeoutSeconds;
}
