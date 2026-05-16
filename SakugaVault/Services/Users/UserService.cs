using Microsoft.EntityFrameworkCore;
using SakugaVault.Data;
using SakugaVault.Models;

namespace SakugaVault.Services.Users;

/// <summary>
/// User persistence service backed by EF Core and MySQL.
/// Normalized lookups live here so auth logic can stay focused on verification and token issuance.
/// </summary>
public sealed class UserService(SakugaVaultDbContext dbContext) : IUserService
{
    public Task<ApplicationUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public Task<ApplicationUser?> GetByIdentifierAsync(string identifier, CancellationToken cancellationToken)
    {
        var normalized = Normalize(identifier);

        return dbContext.Users
            .FirstOrDefaultAsync(user =>
                user.NormalizedEmail == normalized ||
                user.NormalizedUserName == normalized,
                cancellationToken);
    }

    public Task<ApplicationUser?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = Normalize(email);

        return dbContext.Users
            .FirstOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    public Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken)
    {
        var normalizedUserName = Normalize(userName);

        return dbContext.Users
            .AnyAsync(user => user.NormalizedUserName == normalizedUserName, cancellationToken);
    }

    public async Task<ApplicationUser> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        dbContext.Users.Update(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
