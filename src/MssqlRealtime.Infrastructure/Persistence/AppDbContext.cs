using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MssqlRealtime.Core.Modularity;

namespace MssqlRealtime.Infrastructure.Persistence;

/// <summary>The single operator account. No registration flow: seeded once at startup.</summary>
public sealed class AppUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>
/// Local control-plane database (SQLite). Holds the operator account and each module's own
/// configuration — never any customer data, which is only ever read live and streamed.
/// <para>
/// Modules contribute their entities through <see cref="IToolModule.ConfigureDbModel"/>, so
/// adding a tool never means editing this class.
/// </para>
/// </summary>
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IEnumerable<IToolModule> modules)
    : IdentityDbContext<AppUser>(options)
{
    private readonly IToolModule[] _modules = modules.ToArray();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        foreach (var module in _modules)
        {
            module.ConfigureDbModel(builder);
        }
    }
}
