namespace SakugaVault.Services.Redis;

/// <summary>
/// Uniquely identifies one playback-resolution request shape.
/// </summary>
public sealed record StreamCacheKey(
    Guid AnimeId,
    int EpisodeNumber,
    string PreferredLanguage,
    string AudioLanguage,
    string SubtitleLanguage,
    bool AllowRegionalFallback,
    string? ProviderOverride)
{
    public string ToRedisKeySuffix()
    {
        var provider = NormalizeSegment(ProviderOverride ?? "default");
        return $"{AnimeId:D}:{EpisodeNumber}:{NormalizeSegment(PreferredLanguage)}:{NormalizeSegment(AudioLanguage)}:{NormalizeSegment(SubtitleLanguage)}:{AllowRegionalFallback}:{provider}";
    }

    private static string NormalizeSegment(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "default"
            : string.Concat(value.Trim().ToLowerInvariant().Select(character =>
                char.IsLetterOrDigit(character) ? character : '-'));
}
