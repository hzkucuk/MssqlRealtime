using Microsoft.EntityFrameworkCore;
using MssqlRealtime.Core.Common;
using MssqlRealtime.Modules.Mssql.Models;

namespace MssqlRealtime.Modules.Mssql;

/// <summary>
/// Persists server profiles in the shared control-plane database.
/// Takes the base <see cref="DbContext"/> on purpose: the module contributes its entity through
/// <c>ConfigureDbModel</c> and never references the host's concrete context.
/// </summary>
public sealed class EfServerProfileStore(DbContext db) : IServerProfileStore
{
    private DbSet<ServerProfile> Profiles => db.Set<ServerProfile>();

    public async Task<IReadOnlyList<ServerProfile>> GetAllAsync(CancellationToken ct = default) =>
        await Profiles
            .AsNoTracking()
            .OrderBy(p => p.CustomerName)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

    public Task<ServerProfile?> GetAsync(Guid id, CancellationToken ct = default) =>
        Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<ServerProfile> AddAsync(ServerProfile profile, CancellationToken ct = default)
    {
        profile.CreatedAt = DateTimeOffset.UtcNow;
        profile.UpdatedAt = profile.CreatedAt;
        Profiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<Result> UpdateAsync(ServerProfile profile, CancellationToken ct = default)
    {
        if (!await Profiles.AnyAsync(p => p.Id == profile.Id, ct))
        {
            return Result.Failure("Sunucu profili bulunamadı.", "not_found");
        }

        profile.UpdatedAt = DateTimeOffset.UtcNow;
        Profiles.Update(profile);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var deleted = await Profiles.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0
            ? Result.Failure("Sunucu profili bulunamadı.", "not_found")
            : Result.Success();
    }
}
