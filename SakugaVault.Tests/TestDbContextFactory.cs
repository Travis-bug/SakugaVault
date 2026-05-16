using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SakugaVault.Data;

namespace SakugaVault.Tests;

internal static class TestDbContextFactory
{
    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<SakugaVaultDbContext>()
            .UseSqlite(connection)
            .EnableSensitiveDataLogging()
            .Options;

        var dbContext = new SakugaVaultDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        return new SqliteTestDatabase(connection, dbContext);
    }
}

internal sealed class SqliteTestDatabase(
    SqliteConnection connection,
    SakugaVaultDbContext dbContext) : IAsyncDisposable
{
    public SakugaVaultDbContext DbContext { get; } = dbContext;

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
