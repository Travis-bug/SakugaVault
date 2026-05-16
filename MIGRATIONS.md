# SakugaVault Migrations Guide

SakugaVault uses EF Core migrations as the only schema source of truth.

## Development

1. Start MySQL locally.
   `docker-compose up -d mysql`
2. Apply migrations manually when needed.
   `dotnet ef database update --project SakugaVault`
3. Run the API.
   `dotnet run --project SakugaVault`

In Development, the API also calls `Database.MigrateAsync()` during startup before running the development seeder. That keeps fresh local environments usable with minimal setup.

## Production

- Generate and review migrations in source control before deployment.
- Apply migrations as a separate release step or migration job.
- Do not rely on API replicas to auto-migrate production databases.

## Notes

- `EntityBase.CreatedAtUtc` and `EntityBase.UpdatedAtUtc` are explicitly mapped to MySQL `datetime(6)` columns through the DbContext value converters.
- Refresh tokens are persisted in their own table so logout and token rotation can revoke credentials by value.
