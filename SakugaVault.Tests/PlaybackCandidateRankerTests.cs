using SakugaVault.Services.Scraping;

namespace SakugaVault.Tests;

public sealed class PlaybackCandidateRankerTests
{
    [Fact]
    public void SelectBest_ExactRequestWinsBeforeFallbackTree()
    {
        var englishDefault = Candidate("english-default", "en", "en");
        var requestedJapanese = Candidate("requested-japanese", "ja", "ja");

        var selected = PlaybackCandidateRanker.SelectBest(
            [englishDefault, requestedJapanese],
            "ja",
            "ja");

        Assert.Same(requestedJapanese, selected);
    }

    [Fact]
    public void SelectBest_NoExactRequest_UsesDocumentedLanguageFallbackOrder()
    {
        var japaneseEnglish = Candidate("japanese-english", "ja", "en");
        var englishNoSubtitles = Candidate("english-no-subtitles", "en", "off");
        var englishJapanese = Candidate("english-japanese", "en", "ja");

        var selected = PlaybackCandidateRanker.SelectBest(
            [japaneseEnglish, englishJapanese, englishNoSubtitles],
            "ja",
            "off");

        Assert.Same(englishNoSubtitles, selected);
    }

    [Fact]
    public void SelectBest_SameLanguageMatch_PrefersHlsAndResolverPriority()
    {
        var http = Candidate("http", "en", "en", protocol: "HTTP", priority: 1);
        var slowerPriorityHls = Candidate("hls-low-priority", "en", "en", priority: 50);
        var higherPriorityHls = Candidate("hls-high-priority", "en", "en", priority: 10);

        var selected = PlaybackCandidateRanker.SelectBest(
            [http, slowerPriorityHls, higherPriorityHls],
            "en",
            "en");

        Assert.Same(higherPriorityHls, selected);
    }

    [Fact]
    public void SelectBest_IgnoresUnresolvedCandidates()
    {
        var unresolved = Candidate("unresolved", "en", "en", isResolved: false);

        var selected = PlaybackCandidateRanker.SelectBest([unresolved], "en", "en");

        Assert.Null(selected);
    }

    private static StreamScrapeResult Candidate(
        string resolver,
        string audioLanguage,
        string subtitleLanguage,
        string protocol = "HLS",
        int priority = 10,
        bool isResolved = true)
    {
        return new StreamScrapeResult(
            isResolved,
            protocol,
            isResolved ? $"https://streams.test/{resolver}.m3u8" : null,
            resolver,
            "meta/anilist",
            "Resolved.")
        {
            Resolver = resolver,
            ResolverPriority = priority,
            AudioLanguage = audioLanguage,
            SubtitleLanguage = subtitleLanguage
        };
    }
}
