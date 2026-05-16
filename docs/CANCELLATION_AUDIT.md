# Cancellation Token Audit

This note records the reliability pass across SakugaVault service methods.

## Fixed during this pass

- `Program.cs`
  Startup migration and development seeding now use `app.Lifetime.ApplicationStopping` instead of `CancellationToken.None`.
- `Services/Auth/AuthService.cs`
  Refresh-token creation, refresh rotation, logout revocation, and current auth persistence paths all save with the caller token.
- `Services/Catalog/BatchMetadataSyncService.cs`
  The inter-request delay between batch metadata sync calls now respects the caller token.
- `Services/Metadata/MetadataSyncService.cs`
  Consumet metadata HTTP requests and JSON deserialization now use the caller token.
- `Services/Scraping/StreamScraperService.cs`
  Consumet episode lookup and watch-source lookup now use the caller token.
- `Services/Watch/PlaybackResolutionService.cs`
  Per-provider resolution attempts now use linked cancellation tokens with timeout bounds.
- `Services/Watch/WatchHistoryService.cs`
  Cursor paging queries and watch-progress upserts save with the caller token.

## Already correct when audited

- `Services/Users/UserService.cs`
  All EF Core reads and writes were already forwarding the caller token.
- `Services/Watch/WatchPageService.cs`
  Page-load queries were already forwarding the caller token.
- `Services/Catalog/CatalogService.cs`
  Catalog query execution and comment persistence were already forwarding the caller token.
- `Data/SakugaVaultSeeder.cs`
  Seed queries and writes already accepted and forwarded the caller token.
