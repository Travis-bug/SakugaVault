using Microsoft.EntityFrameworkCore;
using SakugaVault.Models;

namespace SakugaVault.Data;

/// <summary>
/// Development-only seed data for local catalog exploration.
/// The seeder is idempotent and assumes migrations already own schema creation.
/// </summary>
public sealed class SakugaVaultSeeder(SakugaVaultDbContext dbContext)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await dbContext.Genres.AnyAsync(cancellationToken) || await dbContext.Anime.AnyAsync(cancellationToken))
        {
            return;
        }

        var genres = new[]
        {
            new Genre { Name = "Action", Slug = "action" },
            new Genre { Name = "Adventure", Slug = "adventure" },
            new Genre { Name = "Comedy", Slug = "comedy" },
            new Genre { Name = "Drama", Slug = "drama" },
            new Genre { Name = "Fantasy", Slug = "fantasy" },
            new Genre { Name = "Mystery", Slug = "mystery" },
            new Genre { Name = "Romance", Slug = "romance" },
            new Genre { Name = "Sci-Fi", Slug = "sci-fi" },
            new Genre { Name = "Sports", Slug = "sports" },
            new Genre { Name = "Supernatural", Slug = "supernatural" }
        };

        dbContext.Genres.AddRange(genres);

        var anime = new[]
        {
            CreateAnime("one-piece", "One Piece", "Pirates, world-building, and impossible ambition on the Grand Line.", 1100, 24, true, true, 1, "gogoanime", "one-piece"),
            CreateAnime("frieren-beyond-journeys-end", "Frieren: Beyond Journey's End", "A long-lived mage learns what remains after the journey ends.", 28, 24, true, true, 2, "gogoanime", "sousou-no-frieren"),
            CreateAnime("dandadan", "Dandadan", "Aliens, ghosts, and velocity collide in a chaotic supernatural action story.", 12, 24, true, true, 3, "gogoanime", "dandadan"),
            CreateAnime("blue-lock", "Blue Lock", "A ruthless striker project turns football into ego-driven survival.", 24, 24, true, true, 4, "gogoanime", "blue-lock"),
            CreateAnime("vinland-saga", "Vinland Saga", "A brutal historical epic about vengeance, war, and the search for peace.", 48, 24, true, true, 5, "gogoanime", "vinland-saga"),
            CreateAnime("the-apothecary-diaries", "The Apothecary Diaries", "A sharp apothecary navigates palace intrigue with deduction and medicine.", 24, 24, true, false, 6, "gogoanime", "kusuriya-no-hitorigoto"),
            CreateAnime("solo-leveling", "Solo Leveling", "The weakest hunter becomes the center of a relentless power climb.", 12, 24, true, true, 7, "gogoanime", "ore-dake-level-up-na-ken"),
            CreateAnime("haikyuu", "Haikyuu!!", "A fast, emotional sports series built on teamwork, momentum, and growth.", 85, 24, true, true, 8, "gogoanime", "haikyuu"),
            CreateAnime("steins-gate", "Steins;Gate", "Time-travel paranoia and tragedy unfold from one reckless discovery.", 24, 24, true, true, 9, "gogoanime", "steinsgate"),
            CreateAnime("your-lie-in-april", "Your Lie in April", "A pianist rediscovers music through a brilliant, chaotic violinist.", 22, 24, true, true, 10, "gogoanime", "shigatsu-wa-kimi-no-uso")
        };

        dbContext.Anime.AddRange(anime);

        var genreMap = genres.ToDictionary(genre => genre.Slug, genre => genre);
        var animeGenreLinks = new Dictionary<string, string[]>
        {
            ["one-piece"] = ["action", "adventure", "comedy", "fantasy"],
            ["frieren-beyond-journeys-end"] = ["adventure", "drama", "fantasy"],
            ["dandadan"] = ["action", "comedy", "supernatural", "sci-fi"],
            ["blue-lock"] = ["action", "sports", "drama"],
            ["vinland-saga"] = ["action", "adventure", "drama"],
            ["the-apothecary-diaries"] = ["drama", "mystery", "romance"],
            ["solo-leveling"] = ["action", "fantasy", "adventure"],
            ["haikyuu"] = ["sports", "comedy", "drama"],
            ["steins-gate"] = ["mystery", "sci-fi", "drama"],
            ["your-lie-in-april"] = ["drama", "romance"]
        };

        foreach (var title in anime)
        {
            foreach (var genreSlug in animeGenreLinks[title.Slug])
            {
                dbContext.AnimeGenres.Add(new AnimeGenre
                {
                    Anime = title,
                    Genre = genreMap[genreSlug]
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Anime CreateAnime(
        string slug,
        string title,
        string synopsis,
        int episodeCount,
        int runtimeMinutes,
        bool subAvailable,
        bool dubAvailable,
        int trendingRank,
        string provider,
        string externalId)
    {
        return new Anime
        {
            Slug = slug,
            Title = title,
            Synopsis = synopsis,
            PosterImageUrl = $"https://images.sakugavault.local/posters/{slug}.jpg",
            BackdropImageUrl = $"https://images.sakugavault.local/backdrops/{slug}.jpg",
            EpisodeCount = episodeCount,
            RuntimeMinutes = runtimeMinutes,
            SubAvailable = subAvailable,
            DubAvailable = dubAvailable,
            TrendingRank = trendingRank,
            MetadataProvider = provider,
            ExternalMetadataId = externalId
        };
    }
}
