namespace SakugaVault.Services.Scraping;

/// <summary>
/// Chooses between independently resolved playback candidates.
/// An exact user preference wins first. Remaining candidates follow the
/// documented streaming fallback tree, with verified HLS sources preferred.
/// </summary>
public static class PlaybackCandidateRanker
{
    public static StreamScrapeResult? SelectBest(
        IEnumerable<StreamScrapeResult> candidates,
        string requestedAudioLanguage,
        string requestedSubtitleLanguage)
    {
        return candidates
            .Where(candidate => candidate.IsResolved && !string.IsNullOrWhiteSpace(candidate.StreamUrl))
            .OrderByDescending(candidate => Score(candidate, requestedAudioLanguage, requestedSubtitleLanguage))
            .ThenBy(candidate => candidate.ResolverPriority)
            .ThenBy(candidate => candidate.Resolver, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static int Score(
        StreamScrapeResult candidate,
        string requestedAudioLanguage,
        string requestedSubtitleLanguage)
    {
        if (!candidate.IsResolved || string.IsNullOrWhiteSpace(candidate.StreamUrl))
        {
            return int.MinValue;
        }

        var audioLanguage = NormalizeLanguage(candidate.AudioLanguage);
        var subtitleLanguage = NormalizeSubtitleLanguage(candidate.SubtitleLanguage);
        var requestedAudio = NormalizeLanguage(requestedAudioLanguage);
        var requestedSubtitle = NormalizeSubtitleLanguage(requestedSubtitleLanguage);
        var score = audioLanguage == requestedAudio && subtitleLanguage == requestedSubtitle
            ? 10_000
            : 0;

        score += (audioLanguage, subtitleLanguage) switch
        {
            ("en", "en") => 600,
            ("en", "off") => 500,
            ("en", "ja") => 400,
            ("ja", "en") => 300,
            ("ja", "ja") => 200,
            ("ja", "off") => 100,
            _ => 0
        };

        if (string.Equals(candidate.PreferredProtocol, "HLS", StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        if (!string.IsNullOrWhiteSpace(candidate.LanguageWarning))
        {
            score -= 1_000;
        }

        return score;
    }

    private static string NormalizeLanguage(string? language)
    {
        var normalized = language?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "unknown";
        }

        var baseLanguage = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return baseLanguage switch
        {
            "eng" or "english" => "en",
            "jpn" or "jp" or "japanese" => "ja",
            _ => baseLanguage
        };
    }

    private static string NormalizeSubtitleLanguage(string? language)
    {
        var normalized = NormalizeLanguage(language);
        return normalized is "none" or "false" or "disabled" ? "off" : normalized;
    }
}
