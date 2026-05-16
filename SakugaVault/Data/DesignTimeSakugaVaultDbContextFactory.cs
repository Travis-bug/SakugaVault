using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SakugaVault.Data;

/// <summary>
/// Design-time DbContext factory for EF Core migrations.
/// This avoids requiring the full web application startup path, which has stricter runtime-only validations.
/// </summary>
public sealed class DesignTimeSakugaVaultDbContextFactory : IDesignTimeDbContextFactory<SakugaVaultDbContext>
{
    public SakugaVaultDbContext CreateDbContext(string[] args)
    {
        var basePath = Directory.GetCurrentDirectory();
        if (Path.GetFileName(basePath) == "SakugaVault")
        {
            basePath = Directory.GetParent(basePath)?.FullName ?? basePath;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("SakugaVault/appsettings.json", optional: false)
            .AddJsonFile("SakugaVault/appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException("Connection string 'MySql' is missing.");

        var optionsBuilder = new DbContextOptionsBuilder<SakugaVaultDbContext>();
        optionsBuilder.UseMySQL(connectionString);

        return new SakugaVaultDbContext(optionsBuilder.Options);
    }
}
