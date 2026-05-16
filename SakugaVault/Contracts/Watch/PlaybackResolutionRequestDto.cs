using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Request to resolve a playable stream for a specific episode.
/// This is kept separate because playback resolution is not the same concern as loading the watch page shell.
/// </summary>
public sealed record PlaybackResolutionRequestDto(
    [property: Range(1, int.MaxValue)] int EpisodeNumber,
    [property: StringLength(16)] string PreferredLanguage = "sub",
    [property: StringLength(64)] string? ProviderOverride = null);
