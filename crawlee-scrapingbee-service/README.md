# SakugaVault Crawlee + ScrapingBee resolver

A JS-heavy playback resolver. It crawls a target streaming site through the
ScrapingBee render API and returns streams in the **same Consumet-compatible
contract** as `scraper-service`, so the C# `StreamScraperService` treats it as
just another entry in `Scrapers__PlaybackResolvers` — no backend code changes,
and the existing Redis cache, stampede lock, rate limiter, secure proxy, and
`PlaybackCandidateRanker` all apply unchanged.

## Trust boundary (important)

- This service is **internal-only**. In `docker-compose.yml` it uses `expose`,
  not `ports`, so it is reachable only by the `api` container on the internal
  Docker network. The browser has no route to it (nginx proxies only `/api/*`).
- `SCRAPINGBEE_API_KEY` is read from this container's own environment and used
  only here. It is never returned in any response and never reaches the C# API
  or the browser. **Do not** add CORS or expose a host port.

This is the deliberate fix for the old spec's frontend fan-out, which would have
forced the scraper endpoints and the ScrapingBee key into browser-shipped code.

## Contract

| Route | Returns |
|---|---|
| `GET /health` | liveness |
| `GET /meta/anilist/{query}` | `{ results: [{ id, title }] }` |
| `GET /meta/anilist/info/{id}` | `{ sourceProvider, episodes: [{ id, number, title }] }` (404 for bare AniList ids) |
| `GET /anime/{provider}/watch?episodeId=…` | `{ headers, sources, audioLanguage, subtitleLanguage, languageSource, languageWarning, subtitles }` |

Episode ids are tagged `sv1:<provider>:<base64url(url)>`, matching
`scraper-service`, so an id from `/info` round-trips back to `/watch`.

### Foreign-language interceptor

The old spec's `subLanguage: 'invalid'` is implemented safely: when a page or
subtitle track looks like hardcoded non-EN/JA content (Spanish, Portuguese,
multi-sub, …) the watch response sets `languageSource: "hardcoded"` and a
`languageWarning`. The C# pipeline already gates `hardcoded` candidates behind
`allowRegionalFallback` and penalizes them (−1000) in the ranker — so foreign
streams are demoted, not silently served, and never override the user's
audio/subtitle preference.

## Configuration

The target site's DOM shape and stream pattern are **configuration**, because
the JS-heavy source is chosen per deployment. See `src/config.ts`. Key vars:

| Var | Purpose |
|---|---|
| `SCRAPINGBEE_API_KEY` | **required**, server-side only |
| `TARGET_BASE_URL` | **required**, base of the target site |
| `TARGET_SEARCH_PATH` | search path; `{query}` is url-encoded title |
| `TARGET_*_SELECTOR` | CSS selectors for search items, episode list, etc. |
| `TARGET_STREAM_URL_REGEX` | capture group for the `.m3u8`/`.mp4` URL |
| `TARGET_SUBTITLE_URL_REGEX` | optional capture group for subtitle URLs |
| `SCRAPINGBEE_RENDER_JS` / `_PREMIUM_PROXY` / `_STEALTH_PROXY` / `_WAIT_MS` | ScrapingBee render/proxy knobs |

The shipped selector/regex defaults are reasonable starting points; tune them to
your chosen target. Run `npm run typecheck` to validate before building.

## Local run

```bash
cd crawlee-scrapingbee-service
npm install
SCRAPINGBEE_API_KEY=... TARGET_BASE_URL=https://example.tld npm run dev
```
