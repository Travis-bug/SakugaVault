using SakugaVault.Contracts.Profile;

namespace SakugaVault.Services.Profile;

/// <summary>
/// Service boundary for the authenticated profile screen.
/// The page combines several slices of user state, so this aggregator keeps that composition out of
/// the controller and out of the React app.
/// </summary>
public interface IProfileService
{
    Task<ProfileSummaryDto?> GetCurrentProfileAsync(Guid userId, CancellationToken cancellationToken);
}
