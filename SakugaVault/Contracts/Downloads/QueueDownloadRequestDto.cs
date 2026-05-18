using System.ComponentModel.DataAnnotations;

namespace SakugaVault.Contracts.Downloads;

/// <summary>
/// Request payload for adding an episode to the user's download queue.
/// The queue is persistence-backed even though the actual file-transfer pipeline is still a later step.
/// </summary>
public sealed record QueueDownloadRequestDto(
    [param: Required(ErrorMessage = "Anime selection is required.")] Guid AnimeId,
    [param: Range(1, int.MaxValue, ErrorMessage = "Episode number must be at least 1.")] int EpisodeNumber,
    [param: Required(ErrorMessage = "Language selection is required."), StringLength(16, ErrorMessage = "Language selection is invalid.")] string PreferredLanguage = "sub",
    [param: Required(ErrorMessage = "Quality selection is required."), StringLength(32, ErrorMessage = "Quality value is too long.")] string Quality = "1080p");
