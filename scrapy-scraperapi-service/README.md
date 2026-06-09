# SakugaVault Scrapy + ScraperAPI resolver

A high-throughput playback resolver. Scrapy spiders crawl a target site through
the **ScraperAPI** proxy (JS render + Cloudflare bypass) and return streams in
the **same Consumet-compatible contract** as `scraper-service` and
`crawlee-scrapingbee-service`, so the C# `StreamScraperService` treats it as just
another entry in `Scrapers__PlaybackResolvers` — no backend code changes, and
the existing Redis cache, stampede lock, rate limiter, secure proxy, and
`PlaybackCandidateRanker` all apply unchanged.

## Trust boundary (important)

- This service is **internal-only**. In `docker-compose.yml` it uses `expose`,
  not `ports`, so only the `api` container reaches it on the internal Docker
  network. The browser has no route to it (nginx proxies only `/api/*`).
- `SCRAPERAPI_API_KEY` is read from this container's own environment and used
  only here, embedded in the ScraperAPI proxy URL. It is never returned in any
  response and never reaches the C# API or the browser. **Do not** add CORS or a
  host port.

## How it runs

Scrapy is built on Twisted's reactor, which can't be driven directly from a
request handler. [`crochet`](https://crochet.readthedocs.io) runs the reactor in
a background thread so each HTTP request can launch a spider and block for its
items (`app/scraper/runner.py`). The HTTP layer is Flask served by waitress.

`ScraperApiProxyMiddleware` (`app/scraper/middlewares.py`) attaches the
ScraperAPI **proxy-mode** URL to every request. Proxy mode keeps `response.url`
as the real target (so relative links resolve), while ScraperAPI renders JS and
bypasses Cloudflare. The "Cloudflare bypass flag" is ScraperAPI's
`ultra_premium` tier (`SCRAPERAPI_ULTRA_PREMIUM=true`).

## Contract

| Route | Returns |
|---|---|
| `GET /health` | liveness + config status |
| `GET /meta/anilist/{query}` | `{ results: [{ id, title }] }` |
| `GET /meta/anilist/info/{id}` | `{ sourceProvider, episodes: [{ id, number, title }] }` (404 for bare AniList ids) |
| `GET /anime/{provider}/watch?episodeId=…` | `{ headers, sources, audioLanguage, subtitleLanguage, languageSource, languageWarning, subtitles }` |

Episode ids are tagged `sv1:<provider>:<base64url(url)>`; search ids use an
`sa:` prefix so a bare AniList id 404s and the C# layer falls through to search.

### Foreign-language interceptor

The old spec's `subLanguage: 'invalid'` is implemented safely: when a page or
subtitle track looks like hardcoded non-EN/JA content (Spanish, Portuguese,
multi-sub, …) the watch response sets `languageSource: "hardcoded"` and a
`languageWarning`. The C# pipeline already gates `hardcoded` candidates behind
`allowRegionalFallback` and penalizes them (−1000) — never silently served.

## Configuration

The target site's DOM (CSS selectors) and stream pattern are **configuration**,
because the crawl target is chosen per deployment. See `app/config.py`. Key vars:

| Var | Purpose |
|---|---|
| `SCRAPERAPI_API_KEY` | **required**, server-side only |
| `TARGET_BASE_URL` | **required**, base of the target site |
| `TARGET_SEARCH_PATH` | search path; `{query}` is url-encoded title |
| `TARGET_*_CSS` | Scrapy CSS selectors (incl. `::attr(href)` / `::text`) |
| `TARGET_STREAM_URL_REGEX` | capture group for the `.m3u8`/`.mp4` URL |
| `TARGET_SUBTITLE_URL_REGEX` | optional capture group for subtitle URLs |
| `SCRAPERAPI_RENDER` / `_ULTRA_PREMIUM` / `_COUNTRY_CODE` | proxy render/anti-bot knobs |

## Local run

```bash
cd scrapy-scraperapi-service
python -m venv .venv && . .venv/bin/activate
pip install -r requirements.txt
SCRAPERAPI_API_KEY=... TARGET_BASE_URL=https://example.tld python -m app.server
```
