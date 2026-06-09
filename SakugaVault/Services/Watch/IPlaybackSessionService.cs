namespace SakugaVault.Services.Watch;

public interface IPlaybackSessionService
{
    string EnsureSession(HttpContext context, Guid userId);
    bool IsAuthorized(HttpContext context, ProxiedPlaybackStream stream);
    void RevokeCurrentSession(HttpContext context);
}
