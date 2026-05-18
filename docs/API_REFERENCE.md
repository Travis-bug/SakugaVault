# SakugaVault API Reference

This document explains the current API surface and how each endpoint fits into the system.

## Auth

`POST /api/auth/register`
- Creates a new user.
- Request body: `displayName`, `userName`, `email`, `password`.
- Response: short-lived JWT access token, its expiry timestamp, and current user profile.
- Side effect: writes a rotated refresh token into an `HttpOnly` cookie.
- Rate limit: `5` requests per `10` minutes per IP.

`POST /api/auth/login`
- Authenticates an existing user with username or email plus password.
- Request body: `identifier`, `password`.
- Response: short-lived JWT access token, its expiry timestamp, and current user profile.
- Side effect: writes a rotated refresh token into an `HttpOnly` cookie.
- Rate limit: `10` requests per minute per IP.

`POST /api/auth/refresh`
- Rotates the `HttpOnly` refresh-token cookie and issues a fresh access token.
- Request body: none.
- Response: new access token, its expiry timestamp, and current user profile.

`POST /api/auth/logout`
- Clears the refresh-token cookie and revokes the stored refresh token if one is present.
- Request body: none.
- Response: `204 No Content`.

`GET /api/auth/me`
- Requires `Authorization: Bearer <token>`.
- Returns the authenticated user's profile.

## Catalog

`GET /api/catalog/home`
- Requires authentication.
- Returns the hero banner plus alphabetized genre rails for the home catalog screen.
- Primary source of truth is the live Consumet provider feed, not MySQL catalog rows.
- If one provider fails, SakugaVault tries the next configured provider before returning an empty catalog.
- Cached in memory under `catalog:home` with a sliding expiration configured by `Catalog:HomeCatalogCacheMinutes`.

`GET /api/catalog/search?q=&limit=`
- Requires authentication.
- Returns discovery results for the React search screen.
- Blank or omitted `q` falls back to the configured live trending feed.
- Search is provider-first when `Catalog:UseLiveProviderCatalog` is enabled.

`POST /api/catalog/comments`
- Requires authentication.
- Creates a new comment for an anime title.
- Request body: `animeId`, `body`.
- Response: `201 Created` with the saved comment payload.

`POST /api/catalog/import-provider`
- Requires authentication.
- Developer/operator endpoint for warming the local shadow catalog from a live provider feed.
- Request body: `provider`, `feed`, `pageCount`, `syncMetadata`.
- Normal use: call this from Swagger after pointing `Scrapers:ConsumetBaseUrl` at a working self-hosted Consumet instance.
- Response: import summary with created/updated counts and per-title results.

`POST /api/catalog/sync-metadata`
- Requires authentication.
- Admin-oriented batch sync endpoint for metadata refresh.
- Request body: optional `animeIds` array. If omitted or empty, SakugaVault syncs every anime that already has `ExternalMetadataId` and `MetadataProvider` configured.
- Response: aggregate result containing per-anime sync status, success count, and failure count.

## Watch

`GET /api/watch/{animeId}`
- Requires authentication.
- Returns the watch page shell: metadata, playback hints, provider-backed season/episode groups, recent comments, and similar anime.
- The watch page now attempts to hydrate live provider metadata on read so the frontend can render clickable season and episode selectors instead of a freeform number box.

`GET /api/watch/history/me?cursor=&pageSize=`
- Requires authentication.
- Returns the current user's saved playback history ordered by `LastWatchedAtUtc DESC, Id DESC`.
- Uses opaque cursor pagination. Response shape: `items`, `nextCursor`, `pageSize`, `hasMore`.

`POST /api/watch/history`
- Requires authentication.
- Upserts watch progress for one anime episode.
- Request body: `animeId`, `episodeNumber`, `positionSeconds`, `durationSeconds`, `completed`.

`POST /api/watch/{animeId}/resolve-playback`
- Requires authentication.
- Attempts to resolve a playable source for a specific episode.
- Request body: `episodeNumber`, `preferredLanguage`, optional `providerOverride`.
- Resolution flow: metadata provider first, then configured fallback providers.
- Response includes `usedFallback` and the final `sourceHost`.
- If every configured provider fails, the endpoint returns a user-safe failure message instead of leaking raw upstream diagnostics.

`POST /api/watch/{animeId}/sync-metadata`
- Requires authentication.
- Refreshes a single anime row from Consumet using `MetadataProvider` and `ExternalMetadataId`.
- Sync updates title text, synopsis, images, episode count, sub/dub flags, and normalized genre joins.

## Downloads

`GET /api/downloads/me`
- Requires authentication.
- Returns the current user's persisted download queue.

`POST /api/downloads`
- Requires authentication.
- Queues an anime episode for later offline processing.
- Request body: `animeId`, `episodeNumber`, `preferredLanguage`, `quality`.
- Rejects duplicates and invalid language or episode combinations.

`DELETE /api/downloads/{downloadId}`
- Requires authentication.
- Removes one queued download request.
- Response: `204 No Content`.

## Profile

`GET /api/profile/me`
- Requires authentication.
- Returns an aggregated profile payload:
  - current user identity
  - continue-watching count
  - completed-entry count
  - comment count
  - queued-download count
  - recent watch history
  - download queue preview

## Infrastructure endpoints

`GET /health`
- Liveness probe for container or platform health checks.

`GET /swagger`
- Development-only interactive API documentation UI.
- Uses the OpenAPI document generated from controllers, DTOs, and XML comments.
