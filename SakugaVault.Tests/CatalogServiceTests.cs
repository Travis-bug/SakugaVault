using Microsoft.Extensions.Caching.Memory;
using SakugaVault.Contracts.Catalog;
using SakugaVault.Models;
using SakugaVault.Options;
using SakugaVault.Services.Catalog;

namespace SakugaVault.Tests;

public sealed class CatalogServiceTests
{
    [Fact]
    public async Task GetHomeCatalogAsync_EmptyCatalog_ReturnsEmptyPlaceholder()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var catalogService = CreateCatalogService(testDatabase.DbContext);

        var result = await catalogService.GetHomeCatalogAsync(CancellationToken.None);

        Assert.Equal(string.Empty, result.HeroBanner.Title);
        Assert.Empty(result.GenreRows);
    }

    [Fact]
    public async Task GetHomeCatalogAsync_WithRankedTitles_SelectsLowestTrendingRankAsHeroBanner()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        await SeedCatalogAsync(testDatabase.DbContext);
        var catalogService = CreateCatalogService(testDatabase.DbContext);

        var result = await catalogService.GetHomeCatalogAsync(CancellationToken.None);

        Assert.Equal("One Piece", result.HeroBanner.Title);
    }

    [Fact]
    public async Task GetHomeCatalogAsync_GroupsTitlesByGenre_ReturnsGenreSpecificRails()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        await SeedCatalogAsync(testDatabase.DbContext);
        var catalogService = CreateCatalogService(testDatabase.DbContext);

        var result = await catalogService.GetHomeCatalogAsync(CancellationToken.None);

        var actionRail = Assert.Single(result.GenreRows, rail => rail.Genre == "Action");
        Assert.All(actionRail.Titles, title =>
            Assert.Contains("Action", FindGenreNamesForTitle(title.Title)));

        IReadOnlyCollection<string> FindGenreNamesForTitle(string title) => title switch
        {
            "One Piece" => ["Action", "Adventure"],
            "Fullmetal Alchemist: Brotherhood" => ["Action", "Drama"],
            "Kaguya-sama: Love Is War" => ["Comedy", "Romance"],
            "Frieren: Beyond Journey's End" => ["Adventure", "Drama", "Fantasy"],
            _ => []
        };
    }

    [Fact]
    public async Task GetHomeCatalogAsync_GenreRailTitles_AreOrderedByTrendingRank()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        await SeedCatalogAsync(testDatabase.DbContext);
        var catalogService = CreateCatalogService(testDatabase.DbContext);

        var result = await catalogService.GetHomeCatalogAsync(CancellationToken.None);
        var adventureRail = Assert.Single(result.GenreRows, rail => rail.Genre == "Adventure");

        Assert.Equal(
            ["One Piece", "Frieren: Beyond Journey's End"],
            adventureRail.Titles.Select(title => title.Title).ToArray());
    }

    [Fact]
    public async Task GetHomeCatalogAsync_MultiGenreTitle_AppearsInEachMatchingRail()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        await SeedCatalogAsync(testDatabase.DbContext);
        var catalogService = CreateCatalogService(testDatabase.DbContext);

        var result = await catalogService.GetHomeCatalogAsync(CancellationToken.None);

        Assert.Contains(result.GenreRows.Single(rail => rail.Genre == "Adventure").Titles, title => title.Title == "Frieren: Beyond Journey's End");
        Assert.Contains(result.GenreRows.Single(rail => rail.Genre == "Drama").Titles, title => title.Title == "Frieren: Beyond Journey's End");
        Assert.Contains(result.GenreRows.Single(rail => rail.Genre == "Fantasy").Titles, title => title.Title == "Frieren: Beyond Journey's End");
    }

    [Fact]
    public async Task GetHomeCatalogAsync_GenreRails_AreAlphabeticallyOrdered()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        await SeedCatalogAsync(testDatabase.DbContext);
        var catalogService = CreateCatalogService(testDatabase.DbContext);

        var result = await catalogService.GetHomeCatalogAsync(CancellationToken.None);

        Assert.Equal(
            result.GenreRows.Select(rail => rail.Genre).OrderBy(title => title).ToArray(),
            result.GenreRows.Select(rail => rail.Genre).ToArray());
    }

    [Fact]
    public async Task GetHomeCatalogAsync_CalledTwiceBeforeCacheExpiry_ReturnsCachedCatalog()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        await SeedCatalogAsync(testDatabase.DbContext);
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var catalogService = new CatalogService(
            testDatabase.DbContext,
            memoryCache,
            Microsoft.Extensions.Options.Options.Create(new CatalogOptions { HomeCatalogCacheMinutes = 5 }));

        var firstResult = await catalogService.GetHomeCatalogAsync(CancellationToken.None);

        testDatabase.DbContext.Anime.Add(new Anime
        {
            Slug = "blue-lock",
            Title = "Blue Lock",
            Synopsis = "A striker program.",
            PosterImageUrl = "https://images.test/bluelock-poster.jpg",
            BackdropImageUrl = "https://images.test/bluelock-backdrop.jpg",
            EpisodeCount = 24,
            RuntimeMinutes = 24,
            SubAvailable = true,
            DubAvailable = true,
            TrendingRank = 0
        });
        await testDatabase.DbContext.SaveChangesAsync();

        var secondResult = await catalogService.GetHomeCatalogAsync(CancellationToken.None);

        Assert.Equal(firstResult.HeroBanner.Title, secondResult.HeroBanner.Title);
    }

    private static CatalogService CreateCatalogService(SakugaVault.Data.SakugaVaultDbContext dbContext)
    {
        return new CatalogService(
            dbContext,
            new MemoryCache(new MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(new CatalogOptions { HomeCatalogCacheMinutes = 5 }));
    }

    private static async Task SeedCatalogAsync(SakugaVault.Data.SakugaVaultDbContext dbContext)
    {
        var action = new Genre { Name = "Action", Slug = "action" };
        var adventure = new Genre { Name = "Adventure", Slug = "adventure" };
        var comedy = new Genre { Name = "Comedy", Slug = "comedy" };
        var drama = new Genre { Name = "Drama", Slug = "drama" };
        var fantasy = new Genre { Name = "Fantasy", Slug = "fantasy" };
        var romance = new Genre { Name = "Romance", Slug = "romance" };

        var onePiece = CreateAnime("one-piece", "One Piece", 1);
        var fmab = CreateAnime("fmab", "Fullmetal Alchemist: Brotherhood", 2);
        var kaguya = CreateAnime("kaguya-sama-love-is-war", "Kaguya-sama: Love Is War", 3);
        var frieren = CreateAnime("frieren", "Frieren: Beyond Journey's End", 4);

        dbContext.Genres.AddRange(action, adventure, comedy, drama, fantasy, romance);
        dbContext.Anime.AddRange(onePiece, fmab, kaguya, frieren);
        dbContext.AnimeGenres.AddRange(
            Link(onePiece, action),
            Link(onePiece, adventure),
            Link(fmab, action),
            Link(fmab, drama),
            Link(kaguya, comedy),
            Link(kaguya, romance),
            Link(frieren, adventure),
            Link(frieren, drama),
            Link(frieren, fantasy));

        await dbContext.SaveChangesAsync();
    }

    private static Anime CreateAnime(string slug, string title, int trendingRank)
    {
        return new Anime
        {
            Slug = slug,
            Title = title,
            Synopsis = $"{title} synopsis",
            PosterImageUrl = $"https://images.test/{slug}-poster.jpg",
            BackdropImageUrl = $"https://images.test/{slug}-backdrop.jpg",
            EpisodeCount = 24,
            RuntimeMinutes = 24,
            SubAvailable = true,
            DubAvailable = trendingRank % 2 == 0,
            TrendingRank = trendingRank
        };
    }

    private static AnimeGenre Link(Anime anime, Genre genre)
    {
        return new AnimeGenre
        {
            Anime = anime,
            Genre = genre
        };
    }
}
