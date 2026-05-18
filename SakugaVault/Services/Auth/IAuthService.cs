using SakugaVault.Contracts.Auth;
using SakugaVault.Services.Common;

namespace SakugaVault.Services.Auth;

/// <summary>
/// Auth service boundary for registration, login, token issuance, refresh rotation, logout revocation, and current-user lookup.
/// This is the heavy-lifting part of auth so controllers can remain thin.
/// </summary>
public interface IAuthService
{
    Task<OperationResult<AuthSessionResult>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken);
    Task<OperationResult<AuthSessionResult>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken);
    Task<OperationResult<AuthSessionResult>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken);
    Task<CurrentUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken);
}
