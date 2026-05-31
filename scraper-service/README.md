# SakugaVault Scraper Service

This service replaces the `riimuru/consumet-api` container for the subset of
Consumet-compatible routes SakugaVault uses. It runs `@consumet/extensions`
directly and keeps the backend HTTP contract stable.

## Why this exists

The old Docker image was marked unhealthy because its built-in healthcheck ran a
missing npm script:

```text
npm run healthcheck-manual
```

The image also returned `500` for some `/meta/anilist/info/{id}` requests. That
caused the backend to fall back to fake episode IDs such as `mock-id-1`, which
cannot reliably resolve real video sources.

This service fixes the ID flow by tagging every returned episode ID with the
native provider that produced it:

```text
sv1:<provider>:<base64url-episode-id>
```

The backend still calls:

```text
/meta/anilist/info/{anilistId}
/meta/anilist/watch?episodeId={taggedEpisodeId}
```

but the Node service can decode the tag and resolve the stream with the same
provider that produced the episode ID.

## Local usage

From the repository root:

```bash
npm run scraper:start
```

The default service URL is:

```text
http://localhost:3100
```

Useful checks:

```bash
curl http://localhost:3100/health
curl http://localhost:3100/meta/anilist/trending
curl http://localhost:3100/meta/anilist/info/20
```

## Environment variables

```text
PORT
  HTTP port. Default: 3100.

HOST
  Bind address. Default: 0.0.0.0.

DEFAULT_ANIME_PROVIDER
  Native provider used for AniList episode mapping. Default: animekai.

PROVIDER_SEARCH_ORDER
  Comma-separated native-provider fallback order used when AniList mapping
  cannot return episodes or when the tagged source provider cannot resolve a
  playable stream. English/Japanese-capable providers are tried before regional
  hard-sub providers.
  Default:
  animekai,hianime,animepahe,kickassanime,animeunity,animesaturn,animesama

ALLOW_REGIONAL_LANGUAGE_FALLBACKS
  Whether providers with regional hardcoded subtitles, such as AnimeUnity or
  AnimeSaturn, can be used when the app requests English/Japanese playback.
  Default: false. Keep this false if a wrong-language stream is worse than no
  stream.

EPISODE_INFO_TIMEOUT_MS
  Timeout for metadata and episode-list operations. Default: 20000.

SOURCE_TIMEOUT_MS
  Timeout for stream-source operations. Default: 20000.

PROVIDER_FALLBACK_TIMEOUT_MS
  Short per-provider timeout for source fallback probes after the tagged
  episode provider fails. Default: 5000.

SCRAPER_CACHE_TTL_MS
  In-memory response cache lifetime. Default: 300000.

SCRAPER_LOG_LEVEL
  Use debug for per-request logs. Default: info.
```

## Supported HTTP routes

```text
GET /health
GET /anime/gogoanime
GET /meta/anilist/trending
GET /meta/anilist/popular
GET /meta/anilist/recent-episodes
GET /meta/anilist/{query}
GET /meta/anilist/info/{anilistId}
GET /meta/anilist/watch?episodeId={taggedEpisodeId}&preferredLanguage=sub&audioLanguage=ja&subtitleLanguage=en&episodeNumber=1&title=Title
GET /anime/{provider}/watch?episodeId={taggedEpisodeId}&preferredLanguage=sub&audioLanguage=ja&subtitleLanguage=en&episodeNumber=1&title=Title
```

`/anime/gogoanime` exists as a lightweight compatibility health route because
the ASP.NET startup check calls that path.

## Backend configuration

When running the ASP.NET backend outside Docker:

```text
Scrapers:ConsumetBaseUrl=http://localhost:3100
```

When running through Docker Compose, the API container calls:

```text
http://scraper:3100
```

## Notes

This is a compatibility layer, not a full reimplementation of every Consumet
route. Add routes only when SakugaVault actually needs them.
