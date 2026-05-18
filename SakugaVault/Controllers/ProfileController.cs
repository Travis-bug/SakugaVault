using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SakugaVault.Contracts.Profile;
using SakugaVault.Extensions;
using SakugaVault.Services.Profile;

namespace SakugaVault.Controllers;

/// <summary>
/// Thin controller for the authenticated profile screen.
/// The controller's only job is auth context and HTTP mapping; profile aggregation stays in ProfileService.
/// </summary>
[ApiController]
[Route("api/profile")]
[Authorize]
public sealed class ProfileController(IProfileService profileService) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType(typeof(ProfileSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfileSummaryDto>> GetMyProfile(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var profile = await profileService.GetCurrentProfileAsync(userId.Value, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        return Ok(profile);
    }
}
