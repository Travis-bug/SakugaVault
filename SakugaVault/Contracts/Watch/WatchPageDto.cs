using SakugaVault.Contracts.Catalog;

namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Full response for the React watch page.
/// The service composes metadata, playback hints, comments, and recommendations here so the
/// controller only has to return a standard HTTP response.
/// </summary>
public sealed record WatchPageDto(
    string Id,
    string Title,
    string Synopsis,
    string PosterImageUrl,
    string BackdropImageUrl,
    int RuntimeMinutes,
    int EpisodeCount,
    bool SubAvailable,
    bool DubAvailable,
    DateTimeOffset? MetadataLastSyncedAtUtc,
    PlaybackDescriptorDto Playback,
    IReadOnlyCollection<CommentDto> Comments,
    IReadOnlyCollection<AnimeCardDto> SimilarAnime);
