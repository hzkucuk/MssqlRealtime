using MssqlRealtime.Core.Alerts;
using MssqlRealtime.Core.Common;
using MssqlRealtime.Core.Modularity;

namespace MssqlRealtime.Core.Abstractions;

/// <summary>
/// Encrypts secrets at rest (SQL passwords, API keys, whatever a later module needs).
/// Backed by ASP.NET Core Data Protection: one portable key ring, no platform lock-in.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plainText);
    Result<string> Unprotect(string cipherText);
}

/// <summary>
/// The real-time transport, as modules see it. Implemented over SignalR in the host so a
/// module never references SignalR and stays unit-testable.
/// <para>
/// Clients subscribe per module and per target; alerts are broadcast to everyone signed in.
/// </para>
/// </summary>
public interface IRealtimePublisher
{
    /// <summary>Pushes a module event to clients watching that module (and that target).</summary>
    Task PublishAsync<T>(string moduleId, string? targetId, string eventName, T payload, CancellationToken ct = default);
}

/// <summary>All modules registered in this build, in display order.</summary>
public interface IModuleRegistry
{
    IReadOnlyList<IToolModule> Modules { get; }
    IToolModule? Find(string moduleId);
    IReadOnlyList<ToolDescriptor> Describe();
}
