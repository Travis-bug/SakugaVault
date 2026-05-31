# Playback Resolver Orchestration

## Purpose

SakugaVault resolves playback on the backend. The browser sends one request to the API, and the API fans that request out to every enabled playback resolver. API keys and provider-specific implementation details stay server-side.

Each resolver is independent. It must fetch its own episode list and use the episode id returned by that same resolver when requesting a stream. Episode ids must never be mixed across providers or resolver services.

## Ranking Order

An exact user request wins first. When an exact match is unavailable, candidates follow this fallback tree:

1. English audio with English subtitles
2. English audio without subtitles
3. English audio with Japanese subtitles
4. Japanese audio with English subtitles
5. Japanese audio with Japanese subtitles
6. Japanese audio without subtitles
7. Any other combination

Reachable HLS streams receive a small preference over direct HTTP files. Candidates with unverified language warnings are penalized.

## Resolver Contract

Resolver services expose the small Consumet-compatible surface already used by SakugaVault:

```text
GET /meta/anilist/info/{anilistId}
GET /meta/anilist/watch?episodeId={nativeEpisodeId}&audioLanguage={language}&subtitleLanguage={language}
GET /anime/gogoanime
```

For non-meta providers, watch routes use:

```text
GET /anime/{provider}/watch?episodeId={nativeEpisodeId}
```

The watch response must return `sources`. It may also return `headers`, `audioLanguage`, `subtitleLanguage`, `languageWarning`, and `subtitles`.

## Configuration

The default local resolver is enabled in `appsettings.json`:

```json
{
  "Scrapers": {
    "PlaybackResolvers": [
      {
        "Name": "consumet-local",
        "BaseUrl": "http://localhost:3100",
        "Enabled": true,
        "Priority": 10
      }
    ]
  }
}
```

Each endpoint may set `RequestTimeoutSeconds` and `RequestHeaders`. Keep credentials in environment variables or a secret store.

## Adding External Scrapers

ScrapingBee and ScraperAPI are proxy infrastructure, not anime playback resolvers. To use either one, deploy a wrapper service that performs provider-specific scraping and exposes the resolver contract above. Then add the wrapper URL as another enabled `PlaybackResolvers` entry.

Do not send ScrapingBee or ScraperAPI credentials to the frontend. Do not commit keys into configuration files.

## Resolver Matrix Check

The repository includes a repeatable API-level matrix check. It resolves episodes 1 through 3 for 10 titles using Japanese audio and English subtitles without regional hard-sub fallbacks:

```bash
MATRIX_IDENTIFIER=your-test-user \
MATRIX_PASSWORD=your-test-password \
npm run scraper:matrix
```

Set `API_BASE_URL` when the API is not listening at `http://localhost:8080`.
