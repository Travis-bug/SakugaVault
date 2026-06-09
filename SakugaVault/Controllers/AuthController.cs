using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SakugaVault.Contracts.Auth;
using SakugaVault.Extensions;
using SakugaVault.Options;
using SakugaVault.Services.Auth;
using SakugaVault.Services.Watch;
using Microsoft.Extensions.Options;

namespace SakugaVault.Controllers;

/// <summary>
/// Auth controller for account creation, login, and current-user retrieval.
/// Its job is routing, refresh-cookie transport, and HTTP status mapping; password verification and token logic live in AuthService.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAuthService authService,
    IOptions<AuthCookieOptions> authCookieOptionsAccessor,
    IHostEnvironment environment,
    IPlaybackSessionService playbackSessionService) : ControllerBase
{
    private readonly AuthCookieOptions authCookieOptions = authCookieOptionsAccessor.Value;

    [AllowAnonymous]
    [EnableRateLimiting("auth-register")]
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterRequestDto request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Registration failed",
                Detail = result.ErrorMessage,
                Status = StatusCodes.Status409Conflict
            });
        }

        SetNoStoreHeaders();
        AppendRefreshTokenCookie(result.Value!.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);
        return Ok(ToResponse(result.Value));
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginRequestDto request, CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Login failed",
                Detail = result.ErrorMessage,
                Status = StatusCodes.Status401Unauthorized
            });
        }

        SetNoStoreHeaders();
        AppendRefreshTokenCookie(result.Value!.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);
        return Ok(ToResponse(result.Value));
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-refresh")]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[authCookieOptions.CookieName];
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Refresh failed",
                Detail = "Your session has expired. Sign in again.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        var result = await authService.RefreshAsync(refreshToken, cancellationToken);
        if (!result.Succeeded)
        {
            ClearRefreshTokenCookie();
            return Unauthorized(new ProblemDetails
            {
                Title = "Refresh failed",
                Detail = result.ErrorMessage,
                Status = StatusCodes.Status401Unauthorized
            });
        }

        SetNoStoreHeaders();
        AppendRefreshTokenCookie(result.Value!.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);
        return Ok(ToResponse(result.Value));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[authCookieOptions.CookieName];
        await authService.LogoutAsync(refreshToken, cancellationToken);
        SetNoStoreHeaders();
        ClearRefreshTokenCookie();
        playbackSessionService.RevokeCurrentSession(HttpContext);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CurrentUserDto>> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userId = User.GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var user = await authService.GetCurrentUserAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        return Ok(user);
    }

    private void SetNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
    }

    private AuthResponseDto ToResponse(AuthSessionResult session)
    {
        return new AuthResponseDto(session.AccessToken, session.AccessTokenExpiresAtUtc, session.User);
    }

    private void AppendRefreshTokenCookie(string refreshToken, DateTimeOffset expiresAtUtc)
    {
        Response.Cookies.Append(authCookieOptions.CookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = !environment.IsDevelopment(),
            SameSite = environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
            Expires = expiresAtUtc.UtcDateTime,
            Path = "/"
        });
    }

    private void ClearRefreshTokenCookie()
    {
        Response.Cookies.Delete(authCookieOptions.CookieName, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = !environment.IsDevelopment(),
            SameSite = environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
            Path = "/"
        });
    }
}
