# SakugaVault API Reference

This document explains the current API surface and how each endpoint fits into the system.

## Auth

`POST /api/auth/register`
- Creates a new user.
- Request body: `displayName`, `userName`, `email`, `password`.
- Response: short-lived JWT access token, rotated refresh token, and current user profile.
- Rate limit: `5` requests per `10` minutes per IP.

`POST /api/auth/login`
- Authenticates an existing user with username or email plus password.
- Request body: `identifier`, `password`.
- Response: short-lived JWT access token, rotated refresh token, and current user profile.
- Rate limit: `10` requests per minute per IP.

`POST /api/auth/refresh`
- Rotates a refresh token and issues a fresh access token.
- Request body: `refreshToken`.
- Response: new access token, new refresh token, and current user profile.

`POST /api/auth/logout`
- Requires authentication.
- Revokes the submitted refresh token.
- Request body: `refreshToken`.
- Response: `204 No Content`.

`GET /api/auth/me`
- Requires `Authorization: Bearer <token>`.
- Returns the authenticated user's profile.

## Catalog

`GET /api/catalog/home`
- Requires authentication.
- Returns the hero banner plus alphabetized genre rails for the home catalog screen.
- Cached in memory under `catalog:home` with a sliding expiration configured by `Catalog:HomeCatalogCacheMinutes`.

`POST /api/catalog/comments`
- Requires authentication.
- Creates a new comment for an anime title.
- Request body: `animeId`, `body`.
- Response: `201 Created` with the saved comment payload.

`POST /api/catalog/sync-metadata`
- Requires authentication.
- Admin-oriented batch sync endpoint for metadata refresh.
- Request body: optional `animeIds` array. If omitted or empty, SakugaVault syncs every anime that already has `ExternalMetadataId` and `MetadataProvider` configured.
- Response: aggregate result containing per-anime sync status, success count, and failure count.

## Watch

`GET /api/watch/{animeId}`
- Requires authentication.
- Returns the watch page shell: metadata, playback hints, recent comments, and similar anime.

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

`POST /api/watch/{animeId}/sync-metadata`
- Requires authentication.
- Refreshes a single anime row from Consumet using `MetadataProvider` and `ExternalMetadataId`.
- Sync updates title text, synopsis, images, episode count, sub/dub flags, and normalized genre joins.

## Infrastructure endpoints

`GET /health`
- Liveness probe for container or platform health checks.

`GET /swagger`
- Development-only interactive API documentation UI.
- Uses the OpenAPI document generated from controllers, DTOs, and XML comments.
