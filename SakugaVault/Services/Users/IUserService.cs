using SakugaVault.Models;

namespace SakugaVault.Services.Users;

/// <summary>
/// Abstraction over user persistence and lookup rules.
/// Auth and future profile features depend on this instead of querying DbContext directly.
/// </summary>
public interface IUserService
{
    Task<ApplicationUser?> GetByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<ApplicationUser?> GetByIdentifierAsync(string identifier, CancellationToken cancellationToken);
    Task<ApplicationUser?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken);
    Task<ApplicationUser> CreateAsync(ApplicationUser user, CancellationToken cancellationToken);
    Task UpdateAsync(ApplicationUser user, CancellationToken cancellationToken);
}
