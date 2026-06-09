/**
 * SakugaVault Crawlee + ScrapingBee resolver.
 *
 * Exposes the same Consumet-compatible surface that StreamScraperService fans
 * out to, so it plugs in as another entry under Scrapers__PlaybackResolvers
 * with no C# changes. The browser never reaches this service; only the C# API
 * does, over the internal Docker network.
 *
 *   GET /health
 *   GET /meta/anilist/{query}        -> search
 *   GET /meta/anilist/info/{id}      -> episodes (404 for bare AniList ids)
 *   GET /anime/{provider}/watch      -> playable sources + subtitles
 */

import http from 'node:http';
import { URL } from 'node:url';
import { loadConfig } from './config.ts';
import { ScrapingBeeClient } from './scrapingbee.ts';
import { TargetAdapter } from './adapter.ts';
import { buildLanguagePlan } from './language.ts';
import { decodeEpisodeId, decodeSearchId, isResolvableSearchId } from './episode-id.ts';
import type { ResolverInfoResponse, ResolverSearchResponse, ResolverWatchResponse } from './types.ts';

const config = loadConfig();
const client = new ScrapingBeeClient(config.scrapingBee);
const adapter = new TargetAdapter(config.target, client);

// Small TTL cache so the C# stampede lock isn't the only thing shielding the
// (paid) ScrapingBee budget from repeated identical lookups.
const cache = new Map<string, { value: unknown; expiresAt: number }>();

async function cached<T>(key: string, factory: () => Promise<T>): Promise<T> {
  const hit = cache.get(key);
  if (hit && hit.expiresAt > Date.now()) {
    return hit.value as T;
  }
  const value = await factory();
  cache.set(key, { value, expiresAt: Date.now() + config.cacheTtlMs });
  return value;
}

function sendJson(response: http.ServerResponse, statusCode: number, payload: unknown): void {
  const body = JSON.stringify(payload);
  response.writeHead(statusCode, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': Buffer.byteLength(body),
    'Cache-Control': 'no-store'
  });
  response.end(body);
}

function decodePathValue(value: string): string {
  return decodeURIComponent(value.replace(/\+/g, '%20')).trim();
}

async function handleSearch(query: string, response: http.ServerResponse): Promise<void> {
  if (!query) {
    sendJson(response, 400, { error: 'missing_query', message: 'Search query is required.' });
    return;
  }
  const payload = await cached<ResolverSearchResponse>(`search:${query}`, async () => ({
    results: await adapter.search(query)
  }));
  sendJson(response, 200, payload);
}

async function handleInfo(id: string, response: http.ServerResponse): Promise<void> {
  if (!id) {
    sendJson(response, 400, { error: 'missing_id', message: 'An id is required.' });
    return;
  }
  // Bare AniList ids aren't resolvable by a title-driven site scraper. Return
  // 404 so StreamScraperService falls through to the /meta/anilist/{title} path.
  if (!isResolvableSearchId(id)) {
    sendJson(response, 404, {
      error: 'unresolvable_id',
      message: 'This resolver maps titles, not raw AniList ids. Use the search route first.'
    });
    return;
  }

  const animePageUrl = decodeSearchId(id);
  const payload = await cached<ResolverInfoResponse>(`info:${animePageUrl}`, async () => ({
    sourceProvider: adapter.providerName,
    episodes: await adapter.listEpisodes(animePageUrl)
  }));

  if (payload.episodes.length === 0) {
    sendJson(response, 404, { error: 'no_episodes', message: 'No episodes were found for this title.' });
    return;
  }
  sendJson(response, 200, payload);
}

async function handleWatch(requestUrl: URL, response: http.ServerResponse): Promise<void> {
  const episodeId = requestUrl.searchParams.get('episodeId');
  if (!episodeId) {
    sendJson(response, 400, { error: 'missing_episode_id', message: 'episodeId query parameter is required.' });
    return;
  }

  const plan = buildLanguagePlan(
    requestUrl.searchParams.get('preferredLanguage') ?? requestUrl.searchParams.get('language'),
    requestUrl.searchParams.get('audioLanguage'),
    requestUrl.searchParams.get('subtitleLanguage')
  );

  const { episodePageUrl } = decodeEpisodeId(episodeId);
  const cacheKey = `watch:${episodePageUrl}:${plan.audioLanguage}:${plan.subtitleLanguage}`;
  const payload = await cached<ResolverWatchResponse>(cacheKey, () => adapter.resolveStream(episodePageUrl, plan));

  if (payload.sources.length === 0) {
    sendJson(response, 404, {
      error: 'no_sources',
      message: 'The resolver could not extract a playable stream from this episode.'
    });
    return;
  }
  sendJson(response, 200, payload);
}

const server = http.createServer(async (request, response) => {
  try {
    if (request.method !== 'GET') {
      sendJson(response, 405, { error: 'method_not_allowed', message: 'Only GET requests are supported.' });
      return;
    }

    const requestUrl = new URL(request.url ?? '/', `http://${request.headers.host ?? 'localhost'}`);
    const path = requestUrl.pathname;

    if (path === '/' || path === '/health') {
      sendJson(response, 200, {
        service: 'sakugavault-crawlee-scrapingbee',
        status: config.configured ? 'online' : 'unconfigured',
        provider: adapter.providerName,
        missing: config.missing
      });
      return;
    }

    // Resolver routes require full configuration. When disabled/unconfigured,
    // return 503 so StreamScraperService records a clean failed candidate.
    if (!config.configured) {
      sendJson(response, 503, {
        error: 'resolver_unconfigured',
        message: `Crawlee/ScrapingBee resolver is not configured. Missing: ${config.missing.join(', ')}.`
      });
      return;
    }

    if (path === '/meta/anilist/watch' || (path.startsWith('/anime/') && path.endsWith('/watch'))) {
      await handleWatch(requestUrl, response);
      return;
    }

    if (path.startsWith('/meta/anilist/info/')) {
      await handleInfo(decodePathValue(path.slice('/meta/anilist/info/'.length)), response);
      return;
    }

    if (path.startsWith('/meta/anilist/')) {
      await handleSearch(decodePathValue(path.slice('/meta/anilist/'.length)), response);
      return;
    }

    sendJson(response, 404, { error: 'route_not_found', message: `No resolver route for ${path}.` });
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Unknown error.';
    console.error(JSON.stringify({ level: 'error', time: new Date().toISOString(), message }));
    sendJson(response, 500, { error: 'resolver_request_failed', message });
  }
});

server.listen(config.port, config.host, () => {
  console.log(JSON.stringify({
    level: 'info',
    time: new Date().toISOString(),
    message: `Crawlee/ScrapingBee resolver listening on http://${config.host}:${config.port}`,
    provider: adapter.providerName,
    target: config.target.baseUrl
  }));
});
