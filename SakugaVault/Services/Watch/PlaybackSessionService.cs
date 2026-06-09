using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace SakugaVault.Services.Watch;

/// <summary>
/// Issues and validates short-lived playback cookies used by proxied stream URLs.
/// A copied stream URL is useless without the matching HttpOnly cookie in the same browser.
/// </summary>
public sealed class PlaybackSessionService(
    IConnectionMultiplexer redis,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<PlaybackSessionService> logger) : IPlaybackSessionService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);
    private const string ContextItemKey = "SakugaVault.PlaybackSessionId";

    public string EnsureSession(HttpContext context, Guid userId)
    {
        if (context.Items.TryGetValue(ContextItemKey, out var item) &&
            item is string sessionFromContext &&
            !string.IsNullOrWhiteSpace(sessionFromContext))
        {
            return sessionFromContext;
        }

        var sessionId = context.Request.Cookies[PlaybackSessionConstants.CookieName];
        if (!IsSafeSessionId(sessionId))
        {
            sessionId = CreateSessionId();
        }

        context.Items[ContextItemKey] = sessionId;
        context.Response.Cookies.Append(PlaybackSessionConstants.CookieName, sessionId!, BuildCookieOptions(timeProvider.GetUtcNow().Add(SessionLifetime)));
        return sessionId!;
    }

    public bool IsAuthorized(HttpContext context, ProxiedPlaybackStream stream)
    {
        var sessionId = context.Request.Cookies[PlaybackSessionConstants.CookieName];
        if (!IsSafeSessionId(sessionId) || !FixedTimeEquals(sessionId!, stream.PlaybackSessionId))
        {
            return false;
        }

        try
        {
            return !redis.GetDatabase().KeyExists(BuildRevokedKey(sessionId!));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Playback session revocation check failed; denying proxied stream access.");
            return false;
        }
    }

    public void RevokeCurrentSession(HttpContext context)
    {
        var sessionId = context.Request.Cookies[PlaybackSessionConstants.CookieName];
        if (IsSafeSessionId(sessionId))
        {
            try
            {
                redis.GetDatabase().StringSet(BuildRevokedKey(sessionId!), "1", SessionLifetime);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to persist playback session revocation.");
            }
        }

        context.Response.Cookies.Delete(PlaybackSessionConstants.CookieName, BuildExpiredCookieOptions());
    }

    private CookieOptions BuildCookieOptions(DateTimeOffset expiresAtUtc) =>
        new()
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = !environment.IsDevelopment(),
            SameSite = environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
            Expires = expiresAtUtc.UtcDateTime,
            Path = "/"
        };

    private CookieOptions BuildExpiredCookieOptions() =>
        new()
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = !environment.IsDevelopment(),
            SameSite = environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
            Path = "/"
        };

    private static string CreateSessionId()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool IsSafeSessionId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 32 and <= 128 &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string BuildRevokedKey(string sessionId) => $"sakugavault:playback:revoked:{sessionId}";
}
