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

        var connectionString = configuration.GetConnectionString("MySql");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // EF Core only needs a parseable provider string to build migrations at design time.
            // Runtime still requires a real MySQL connection string from environment configuration.
            connectionString = "Server=localhost;Port=3306;Database=sakugavault;User=root;Password=;";
        }

        var optionsBuilder = new DbContextOptionsBuilder<SakugaVaultDbContext>();
        optionsBuilder.UseMySQL(connectionString);

        return new SakugaVaultDbContext(optionsBuilder.Options);
    }
}
