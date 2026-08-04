using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MssqlRealtime.Core.Modularity;

/// <summary>
/// A tool. This is the seam the whole product is built around: adding a capability means
/// adding a module, never editing the host.
/// <para>
/// A module owns its data model, its background work, its HTTP endpoints and its alert rules.
/// The host gives it identity, storage, real-time transport, alerting and notifications for free.
/// The matching front-end module lives in <c>app/src/lib/modules/&lt;Id&gt;/</c> and is discovered
/// by the same <see cref="Id"/>.
/// </para>
/// </summary>
public interface IToolModule
{
    /// <summary>Stable slug: DI keys, API route prefix, SignalR group prefix and UI folder all use it.</summary>
    string Id { get; }

    string Title { get; }

    /// <summary>Emoji or icon key the client resolves. Kept as data so the host ships no module assets.</summary>
    string Icon { get; }

    /// <summary>Sort order on the tool dashboard.</summary>
    int Order => 100;

    string Version => "1.0.0";

    /// <summary>Register the module's own services. Called once at startup.</summary>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Add the module's entities to the shared control-plane database.</summary>
    void ConfigureDbModel(ModelBuilder modelBuilder)
    {
    }

    /// <summary>
    /// Map endpoints under <c>/api/modules/{Id}</c>. The group already requires authentication.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder routes)
    {
    }

    /// <summary>What the client is told about this module, so the UI can be driven by data.</summary>
    ToolDescriptor Describe() => new()
    {
        Id = Id,
        Title = Title,
        Icon = Icon,
        Order = Order,
        Version = Version
    };
}

/// <summary>Serialized to clients at <c>/api/modules</c>; drives the tool list on every platform.</summary>
public sealed record ToolDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public int Order { get; init; }
    public string Version { get; init; } = "1.0.0";

    /// <summary>Free-form capability flags a client may branch on, e.g. "targets", "alerts", "actions".</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    /// <summary>Optional short description shown under the tool title.</summary>
    public string? Description { get; init; }
}
