using SakugaVault.Contracts.Watch;
using SakugaVault.Models;
using SakugaVault.Services.Watch;

namespace SakugaVault.Tests;

public sealed class WatchHistoryServiceTests
{
    [Fact]
    public async Task UpsertAsync_BufferAvailable_WritesRedisBufferInsteadOfMysql()
    {
        await using var testDatabase = await TestDbContextFactory.CreateAsync();
        var anime = new Anime
        {
            Slug = "buffered-title",
            Title = "Buffered Title",
            Synopsis = "Buffered progress.",
            PosterImageUrl = "https://images.test/buffered-poster.jpg",
            BackdropImageUrl = "https://images.test/buffered-backdrop.jpg",
            EpisodeCount = 12,
            RuntimeMinutes = 24,
            SubAvailable = true,
            DubAvailable = false,
            MetadataProvider = "meta/anilist",
            ExternalMetadataId = "200"
        };
        testDatabase.DbContext.Anime.Add(anime);
        await testDatabase.DbContext.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var buffer = new StubWatchProgressBuffer { WriteSucceeds = true };
        var service = new WatchHistoryService(testDatabase.DbContext, TimeProvider.System, buffer);

        var result = await service.UpsertAsync(
            userId,
            new UpsertWatchHistoryRequestDto(anime.Id, 2, 123, 1440, false),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(buffer.LastEntry);
        Assert.Equal(userId, buffer.LastEntry!.UserId);
        Assert.Equal(anime.Id, buffer.LastEntry.AnimeId);
        Assert.Empty(testDatabase.DbContext.WatchHistoryEntries);
    }

    private sealed class StubWatchProgressBuffer : IWatchProgressBuffer
    {
        public bool WriteSucceeds { get; init; }
        public WatchProgressEntry? LastEntry { get; private set; }

        public Task<bool> WriteAsync(WatchProgressEntry entry, CancellationToken cancellationToken)
        {
            LastEntry = entry;
            return Task.FromResult(WriteSucceeds);
        }

        public Task<WatchProgressEntry?> ReadAsync(WatchProgressKey key, CancellationToken cancellationToken)
        {
            return Task.FromResult<WatchProgressEntry?>(null);
        }

        public Task<IReadOnlyList<WatchProgressKey>> GetDirtyKeysAsync(Guid? userId, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<WatchProgressKey>>([]);
        }

        public Task ClearAsync(WatchProgressKey key, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
