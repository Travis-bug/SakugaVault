using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SakugaVault.Models;

namespace SakugaVault.Data;

/// <summary>
/// EF Core database context for SakugaVault.
/// This file defines the relational model that MySQL will store, which is why the user/auth/watch foundations are added here before higher-level features.
/// </summary>
public sealed class SakugaVaultDbContext(DbContextOptions<SakugaVaultDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<DateTimeOffset, DateTime> DateTimeOffsetConverter =
        new(value => value.UtcDateTime, value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableDateTimeOffsetConverter =
        new(
            value => value.HasValue ? value.Value.UtcDateTime : null,
            value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);

    public DbSet<Anime> Anime => Set<Anime>();
    public DbSet<AnimeComment> AnimeComments => Set<AnimeComment>();
    public DbSet<AnimeGenre> AnimeGenres => Set<AnimeGenre>();
    public DbSet<DownloadRequest> DownloadRequests => Set<DownloadRequest>();
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<WatchHistoryEntry> WatchHistoryEntries => Set<WatchHistoryEntry>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditStamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditStamps();
        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(builder =>
        {
            builder.ToTable("Users");
            builder.HasKey(user => user.Id);
            builder.HasIndex(user => user.NormalizedEmail).IsUnique();
            builder.HasIndex(user => user.NormalizedUserName).IsUnique();
            builder.Property(user => user.UserName).HasMaxLength(64).IsRequired();
            builder.Property(user => user.NormalizedUserName).HasMaxLength(64).IsRequired();
            builder.Property(user => user.DisplayName).HasMaxLength(120).IsRequired();
            builder.Property(user => user.Email).HasMaxLength(256).IsRequired();
            builder.Property(user => user.NormalizedEmail).HasMaxLength(256).IsRequired();
            builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
        });

        modelBuilder.Entity<Anime>(builder =>
        {
            builder.ToTable("Anime");
            builder.HasKey(anime => anime.Id);
            builder.HasIndex(anime => anime.Slug).IsUnique();
            builder.HasIndex(anime => anime.TrendingRank);
            builder.Property(anime => anime.Slug).HasMaxLength(160).IsRequired();
            builder.Property(anime => anime.Title).HasMaxLength(200).IsRequired();
            builder.Property(anime => anime.Synopsis).HasMaxLength(4000).IsRequired();
            builder.Property(anime => anime.PosterImageUrl).HasMaxLength(500).IsRequired();
            builder.Property(anime => anime.BackdropImageUrl).HasMaxLength(500).IsRequired();
            builder.Property(anime => anime.MetadataProvider).HasMaxLength(64);
            builder.Property(anime => anime.ExternalMetadataId).HasMaxLength(128);
        });

        modelBuilder.Entity<Genre>(builder =>
        {
            builder.ToTable("Genres");
            builder.HasKey(genre => genre.Id);
            builder.HasIndex(genre => genre.Slug).IsUnique();
            builder.HasIndex(genre => genre.Name).IsUnique();
            builder.Property(genre => genre.Name).HasMaxLength(80).IsRequired();
            builder.Property(genre => genre.Slug).HasMaxLength(80).IsRequired();
        });

        modelBuilder.Entity<AnimeGenre>(builder =>
        {
            builder.ToTable("AnimeGenres");
            builder.HasKey(link => new { link.AnimeId, link.GenreId });

            builder.HasOne(link => link.Anime)
                .WithMany(anime => anime.AnimeGenres)
                .HasForeignKey(link => link.AnimeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(link => link.Genre)
                .WithMany(genre => genre.AnimeGenres)
                .HasForeignKey(link => link.GenreId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AnimeComment>(builder =>
        {
            builder.ToTable("AnimeComments");
            builder.HasKey(comment => comment.Id);
            builder.HasIndex(comment => new { comment.AnimeId, comment.CreatedAtUtc });
            builder.Property(comment => comment.Body).HasMaxLength(2000).IsRequired();

            builder.HasOne(comment => comment.Anime)
                .WithMany(anime => anime.Comments)
                .HasForeignKey(comment => comment.AnimeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(comment => comment.User)
                .WithMany(user => user.Comments)
                .HasForeignKey(comment => comment.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RefreshToken>(builder =>
        {
            builder.ToTable("RefreshTokens");
            builder.HasKey(token => token.Id);
            builder.HasIndex(token => token.Token).IsUnique();
            builder.HasIndex(token => new { token.UserId, token.ExpiresAtUtc });
            builder.Property(token => token.Token).HasMaxLength(256).IsRequired();

            builder.HasOne(token => token.User)
                .WithMany(user => user.RefreshTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DownloadRequest>(builder =>
        {
            builder.ToTable("DownloadRequests");
            builder.HasKey(request => request.Id);
            builder.HasIndex(request => new { request.UserId, request.CreatedAtUtc });
            builder.HasIndex(request => new { request.UserId, request.AnimeId, request.EpisodeNumber, request.PreferredLanguage }).IsUnique();
            builder.Property(request => request.PreferredLanguage).HasMaxLength(16).IsRequired();
            builder.Property(request => request.Quality).HasMaxLength(32).IsRequired();
            builder.Property(request => request.Status).HasMaxLength(32).IsRequired();

            builder.HasOne(request => request.Anime)
                .WithMany(anime => anime.DownloadRequests)
                .HasForeignKey(request => request.AnimeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(request => request.User)
                .WithMany(user => user.DownloadRequests)
                .HasForeignKey(request => request.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WatchHistoryEntry>(builder =>
        {
            builder.ToTable("WatchHistoryEntries");
            builder.HasKey(entry => entry.Id);
            builder.HasIndex(entry => new { entry.UserId, entry.LastWatchedAtUtc });
            builder.HasIndex(entry => new { entry.UserId, entry.AnimeId, entry.EpisodeNumber }).IsUnique();

            builder.HasOne(entry => entry.Anime)
                .WithMany(anime => anime.WatchHistoryEntries)
                .HasForeignKey(entry => entry.AnimeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(entry => entry.User)
                .WithMany(user => user.WatchHistoryEntries)
                .HasForeignKey(entry => entry.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureUtcDateTimeOffsets(modelBuilder);
    }

    private void ApplyAuditStamps()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<RefreshToken>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<WatchHistoryEntry>().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            entry.Entity.LastWatchedAtUtc = now;
        }
    }

    private static void ConfigureUtcDateTimeOffsets(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetColumnType("datetime(6)");
                    property.SetValueConverter(DateTimeOffsetConverter);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetColumnType("datetime(6)");
                    property.SetValueConverter(NullableDateTimeOffsetConverter);
                }
            }
        }
    }
}
