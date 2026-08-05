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

    // Platform-level tables: shared by every tool, so they live here rather than in a module.
    public DbSet<NotificationChannelSetting> NotificationChannelSettings => Set<NotificationChannelSetting>();
    public DbSet<NotificationChannelState> NotificationChannelStates => Set<NotificationChannelState>();
    public DbSet<AlertRecord> AlertRecords => Set<AlertRecord>();
    public DbSet<NotificationOutboxEntry> NotificationOutbox => Set<NotificationOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<NotificationChannelSetting>(e =>
        {
            e.ToTable("NotificationChannelSettings");
            e.HasKey(x => x.Id);
            e.Property(x => x.ChannelId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Key).HasMaxLength(64).IsRequired();
            e.Property(x => x.Value).HasMaxLength(4000);
            e.HasIndex(x => new { x.ChannelId, x.Key }).IsUnique();
        });

        builder.Entity<NotificationChannelState>(e =>
        {
            e.ToTable("NotificationChannelStates");
            e.HasKey(x => x.ChannelId);
            e.Property(x => x.ChannelId).HasMaxLength(64);
            e.Property(x => x.MinimumSeverity).HasConversion<int>();
        });

        builder.Entity<AlertRecord>(e =>
        {
            e.ToTable("AlertRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.ModuleId).HasMaxLength(64).IsRequired();
            e.Property(x => x.TargetId).HasMaxLength(128).IsRequired();
            e.Property(x => x.TargetName).HasMaxLength(200).IsRequired();
            e.Property(x => x.GroupName).HasMaxLength(200);
            e.Property(x => x.RuleId).HasMaxLength(64).IsRequired();
            e.Property(x => x.RuleTitle).HasMaxLength(128).IsRequired();
            e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.Unit).HasMaxLength(16);
            e.Property(x => x.Severity).HasConversion<int>();

            // "What is firing right now" and "what happened lately" are the only two queries.
            e.HasIndex(x => new { x.ModuleId, x.TargetId, x.RuleId, x.ClearedAtUtc });
            e.HasIndex(x => x.RaisedAtUtc);
        });

        builder.Entity<NotificationOutboxEntry>(e =>
        {
            e.ToTable("NotificationOutbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.ChannelId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(400);
            e.Property(x => x.LastError).HasMaxLength(1000);

            // The retry sweep asks exactly one question: what is due now and not abandoned?
            e.HasIndex(x => new { x.AbandonedUtc, x.NextAttemptUtc });
        });


        foreach (var module in _modules)
        {
            module.ConfigureDbModel(builder);
        }
    }
}
