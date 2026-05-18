# SakugaVault Backend File Guide

This is the study map for the backend after the MVC-to-API refactor.

## Read order

1. `SakugaVault/Program.cs`
2. `SakugaVault/Extensions/ServiceCollectionExtensions.cs`
3. `SakugaVault/Data/SakugaVaultDbContext.cs`
4. `SakugaVault/Models/*`
5. `SakugaVault/Controllers/*`
6. `SakugaVault/Services/*`
7. `SakugaVault/Contracts/*`

## What each folder does

`SakugaVault/Data`
- Holds `SakugaVaultDbContext`, the EF Core entry point for MySQL.
- `DesignTimeSakugaVaultDbContextFactory.cs`: lets `dotnet ef` build the DbContext without going through the runtime startup path.
- `SakugaVaultSeeder.cs`: optional development-only seed data for genres and anime. The production path is provider-first, so this stays off unless you explicitly enable `Catalog:EnableDevelopmentSeedData`.

`SakugaVault/Models`
- Defines the persisted relational model.
- `ApplicationUser.cs`: account record used by auth and watch history.
- `Anime.cs`: anime metadata record.
- `Genre.cs`: catalog taxonomy.
- `AnimeGenre.cs`: many-to-many join between anime and genres.
- `AnimeComment.cs`: stored comments for watch pages.
- `DownloadRequest.cs`: persisted queue item for offline-download planning.
- `WatchHistoryEntry.cs`: playback progress per user and episode.
- `RefreshToken.cs`: persisted token rows used for refresh rotation and logout revocation.
- `EntityBase.cs`: shared `Id`, `CreatedAtUtc`, and `UpdatedAtUtc` fields.

`SakugaVault/Contracts`
- Defines the external API payloads.
- `Contracts/Auth`: request/response models for registration, login, and session hydration.
- `Contracts/Catalog`: catalog screen DTOs, comment posting contracts, and batch metadata sync contracts.
- `Contracts/Catalog/ImportCatalogRequestDto.cs`: developer-facing request contract for pulling a provider feed into the local catalog.
- `Contracts/Catalog/CatalogImportResultDto.cs`: import summary returned by Swagger/operator workflows.
- `Contracts/Downloads`: persisted download-queue request and response DTOs.
- `Contracts/Profile`: aggregated profile-screen payloads.
- `Contracts/Watch`: watch-page, cursor history, metadata-sync, playback-resolution, and season/episode selector DTOs.
- `Contracts/Common/CursorPagedResult.cs`: generic cursor-pagination wrapper used by watch history.

`SakugaVault/Controllers`
- Defines the HTTP API surface.
- `AuthController.cs`: registration, login, and current-user routes.
- `CatalogController.cs`: catalog home, search, comment posting, and batch metadata sync routes.
- `CatalogController.cs`: also exposes the developer-facing provider import route used to populate the catalog from a live upstream feed.
- `DownloadsController.cs`: persisted download-queue routes.
- `ProfileController.cs`: aggregated profile route.
- `WatchController.cs`: watch page plus history, metadata sync, and playback resolution routes.

`SakugaVault/Services`
- Holds business logic and orchestration.
- `Services/Auth/AuthService.cs`: registration, login, password hashing, JWT issuance, refresh-token rotation, and logout revocation.
- `Services/Users/UserService.cs`: user lookups and persistence.
- `Services/Catalog/CatalogService.cs`: hero-banner and genre-row composition plus comment posting.
- `Services/Catalog/CatalogService.cs`: provider-first home/search composition plus shadow-title upserts and comment posting.
- `Services/Catalog/CatalogImportService.cs`: operator workflow for warming shadow rows from a provider feed and optionally chaining metadata sync.
- `Services/Downloads/DownloadQueueService.cs`: persisted queue reads, duplicate prevention, and queued-download creation/removal.
- `Services/Profile/ProfileService.cs`: profile-page aggregation across auth, history, comments, and download queue.
- `Services/Catalog/BatchMetadataSyncService.cs`: sequential metadata refresh orchestration with a configurable delay between upstream calls.
- `Services/Watch/WatchPageService.cs`: watch-page screen composition plus live provider metadata hydration for the season/episode browser.
- `Services/Watch/WatchHistoryService.cs`: cursor-paged history reads plus single-query progress upserts.
- `Services/Watch/PlaybackResolutionService.cs`: coordinates primary-provider and fallback-provider stream resolution.
- `Services/Metadata/MetadataSyncService.cs`: real Consumet-backed metadata sync plus catalog-cache invalidation.
- `Services/Scraping/AnimeProviderClient.cs`: shared Consumet client for feed, search, and info lookups across multiple providers.
- `Services/Scraping/StreamScraperService.cs`: Consumet-backed episode lookup and source selection.
- `Services/Common/OperationResult.cs`: simple service-layer success/failure wrapper.

`SakugaVault/Extensions`
- `ServiceCollectionExtensions.cs`: registers all layers and cross-cutting infrastructure.
- `ClaimsPrincipalExtensions.cs`: reads the authenticated user id from the claims carried by the access token.
- `CorsPolicyNames.cs`: central named CORS policy values.
- `HttpContextItemKeys.cs`: shared key names for request-scoped items such as the correlation id.

`SakugaVault/Middleware`
- `CorrelationIdMiddleware.cs`: assigns or forwards `X-Correlation-Id`, stores it on the request, and opens a logging scope.

`SakugaVault/Infrastructure/Logging`
- `LoggingDelegatingHandler.cs`: logs outbound scraper-client HTTP calls with method, URL, status, and latency.

 `SakugaVault/Options`
- Strongly typed configuration classes.
- `FrontendOptions.cs`: allowed frontend origins for CORS.
- `ScraperOptions.cs`: Consumet base URL, request timeout, fallback providers, and inter-request delay.
- `JwtOptions.cs`: token issuer, audience, key sourcing, and access-token expiry.
- `AuthCookieOptions.cs`: refresh-token cookie name and lifetime.
- `CatalogOptions.cs`: home-catalog cache duration plus the development seeding toggle.

Repository root
- `docker-compose.yml`: local runtime for MySQL, the API, and the React frontend. It also supports an optional `consumet-local-source` profile when you already have a lawful local Consumet source tree in `vendor/consumet`.

## Startup flow

1. `Program.cs` builds the host and calls `AddApiLayer`, `AddApplicationLayer`, and `AddInfrastructureLayer`.
2. `AddApiLayer` binds options, validates JWT secret and CORS rules, enables rate limiting, and configures JWT bearer authentication plus OpenAPI.
3. `AddInfrastructureLayer` wires MySQL, the named `scraper-client`, the outbound logging handler, password hashing, and `TimeProvider`.
4. In Development, `Program.cs` applies migrations and only runs `SakugaVaultSeeder` when development seed data is explicitly enabled.
5. `CorrelationIdMiddleware` runs early so downstream logs and outbound HTTP calls carry a shared correlation id.

## Core architectural decisions

- Controllers are thin: they validate HTTP concerns, read auth context, call services, and return status codes.
- Services are fat: they contain orchestration, shaping, and workflow logic.
- DTOs are separate from models: models represent database state; DTOs represent API contracts.
- Consumet is the content source of truth for catalog, metadata, and playback resolution.
- MySQL stores only relational application data and shadow title records, never video blobs.
- Playback resolution is a separate workflow so stream hosts remain replaceable and legally isolated from the core app.
- Secrets come from environment variables, not committed config.
- Development may auto-migrate and seed; production should use a separate migration step.
