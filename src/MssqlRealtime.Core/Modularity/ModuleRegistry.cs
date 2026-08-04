using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MssqlRealtime.Core.Abstractions;

namespace MssqlRealtime.Core.Modularity;

public sealed class ModuleRegistry(IEnumerable<IToolModule> modules) : IModuleRegistry
{
    public IReadOnlyList<IToolModule> Modules { get; } = modules.OrderBy(m => m.Order).ThenBy(m => m.Title).ToList();

    public IToolModule? Find(string moduleId) =>
        Modules.FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ToolDescriptor> Describe() => Modules.Select(m => m.Describe()).ToList();
}

public static class ModuleRegistrationExtensions
{
    /// <summary>
    /// Registers a tool module: its services, and the module itself for endpoint mapping and
    /// model configuration later in startup. This single call is all a new tool costs the host.
    /// </summary>
    public static IServiceCollection AddToolModule<TModule>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TModule : class, IToolModule, new()
    {
        var module = new TModule();
        services.AddSingleton<IToolModule>(module);
        module.ConfigureServices(services, configuration);
        return services;
    }

    /// <summary>Maps every module under <c>/api/modules/{id}</c>.</summary>
    public static IEndpointRouteBuilder MapToolModules(this IEndpointRouteBuilder endpoints)
    {
        var registry = endpoints.ServiceProvider.GetRequiredService<IModuleRegistry>();

        foreach (var module in registry.Modules)
        {
            var group = endpoints.MapGroup($"/api/modules/{module.Id}").RequireAuthorization();
            module.MapEndpoints(group);
        }

        return endpoints;
    }
}
