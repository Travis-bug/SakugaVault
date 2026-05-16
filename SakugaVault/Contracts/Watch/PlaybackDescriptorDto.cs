namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Describes how playback should be resolved without exposing host-specific scraper internals.
/// The API returns playback strategy metadata instead of embedding stream files directly.
/// </summary>
public sealed record PlaybackDescriptorDto(
    string PreferredProtocol,
    bool ResolveOnPlay,
    string ResolverMode);
