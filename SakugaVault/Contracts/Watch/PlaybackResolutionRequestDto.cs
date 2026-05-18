using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Watch;

/// <summary>
/// Request to resolve a playable stream for a specific episode.
/// This is kept separate because playback resolution is not the same concern as loading the watch page shell.
/// </summary>
public sealed record PlaybackResolutionRequestDto(
    [param: Range(1, int.MaxValue, ErrorMessage = "Episode number must be at least 1.")] int EpisodeNumber,
    [param: StringLength(16, ErrorMessage = "Preferred language value is too long.")] string PreferredLanguage = "sub",
    [param: StringLength(64, ErrorMessage = "Provider override value is too long.")] string? ProviderOverride = null);
