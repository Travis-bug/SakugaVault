#!/usr/bin/env node

/**
 * SakugaVault scraper service.
 *
 * This service exposes the small Consumet-compatible HTTP surface the ASP.NET
 * backend already calls, but it uses the installed @consumet/extensions package
 * directly instead of depending on the riimuru/consumet-api Docker image.
 *
 * Important ID rule:
 * AniList is metadata. Native anime providers are stream sources. Episode IDs
 * returned by this service are tagged as sv1:<provider>:<base64url-id> so the
 * /meta/anilist/watch endpoint can resolve each episode with the same provider
 * that produced the ID.
 */

const http = require('node:http');
const { Buffer } = require('node:buffer');
const { URL } = require('node:url');
const { ANIME, META, StreamingServers, SubOrSub } = require('@consumet/extensions');
const { MegaUp } = require('@consumet/extensions/dist/extractors/megaup');

const port = Number.parseInt(process.env.PORT || process.env.SCRAPER_PORT || '3100', 10);
const host = process.env.HOST || '0.0.0.0';
const defaultProviderKey = normalizeProviderKey(process.env.DEFAULT_ANIME_PROVIDER || 'animekai');
const providerSearchOrder = parseCsv(process.env.PROVIDER_SEARCH_ORDER)
  .map(normalizeProviderKey)
  .filter(Boolean);
const episodeInfoTimeoutMs = Number.parseInt(process.env.EPISODE_INFO_TIMEOUT_MS || '20000', 10);
const sourceTimeoutMs = Number.parseInt(process.env.SOURCE_TIMEOUT_MS || '20000', 10);
const providerFallbackTimeoutMs = Number.parseInt(process.env.PROVIDER_FALLBACK_TIMEOUT_MS || '5000', 10);
const cacheTtlMs = Number.parseInt(process.env.SCRAPER_CACHE_TTL_MS || '300000', 10);
const logLevel = process.env.SCRAPER_LOG_LEVEL || 'info';
const allowRegionalLanguageFallbacks = String(process.env.ALLOW_REGIONAL_LANGUAGE_FALLBACKS || 'false').toLowerCase() === 'true';
const regionalHardSubProviders = new Set(['animeunity', 'animesaturn', 'animesama']);
const providerFactories = new Map([
  ['animekai', () => new ANIME.AnimeKai()],
  ['hianime', () => new ANIME.Hianime()],
  ['zoro', () => new ANIME.Hianime()],
  ['animepahe', () => new ANIME.AnimePahe()],
  ['kickassanime', () => new ANIME.KickAssAnime()],
  ['animeunity', () => new ANIME.AnimeUnity()],
  ['animesaturn', () => new ANIME.AnimeSaturn()],
  ['animesama', () => new ANIME.AnimeSama()]
]);
const installedProviderKeys = [
  'animekai',
  'hianime',
  'animepahe',
  'kickassanime',
  'animeunity',
  'animesaturn',
  'animesama'
];

const infoCache = new Map();
const searchCache = new Map();

const server = http.createServer(async (request, response) => {
  const startedAt = Date.now();

  try {
    if (request.method !== 'GET') {
      sendJson(response, 405, { error: 'method_not_allowed', message: 'Only GET requests are supported.' });
      return;
    }

    const requestUrl = new URL(request.url || '/', `http://${request.headers.host || 'localhost'}`);
    const path = requestUrl.pathname;
    debug('incoming request', { path, query: requestUrl.search });

    if (path === '/' || path === '/health' || path === '/anime/gogoanime') {
      sendJson(response, 200, {
        service: 'sakugavault-scraper',
        status: 'online',
        defaultProvider: defaultProviderKey,
        availableProviders: installedProviderKeys,
        providerSearchOrder: getProviderSearchOrder(),
        compatibleRoutes: [
          '/meta/anilist/trending',
          '/meta/anilist/{query}',
          '/meta/anilist/info/{id}',
          '/meta/anilist/watch?episodeId={taggedEpisodeId}'
        ]
      });
      return;
    }

    if (path === '/meta/anilist/trending') {
      await handleTrending(requestUrl, response);
      return;
    }

    if (path === '/meta/anilist/popular') {
      await handlePopular(requestUrl, response);
      return;
    }

    if (path === '/meta/anilist/recent-episodes') {
      await handleRecentEpisodes(requestUrl, response);
      return;
    }

    if (path.startsWith('/meta/anilist/info/')) {
      const id = decodePathValue(path.slice('/meta/anilist/info/'.length));
      await handleAnilistInfo(id, response);
      return;
    }

    if (path === '/meta/anilist/watch') {
      await handleWatch(requestUrl, response, defaultProviderKey);
      return;
    }

    if (path.startsWith('/anime/') && path.endsWith('/watch')) {
      const provider = decodePathValue(path.slice('/anime/'.length, -'/watch'.length));
      await handleWatch(requestUrl, response, normalizeProviderKey(provider));
      return;
    }

    if (path.startsWith('/meta/anilist/')) {
      const query = decodePathValue(path.slice('/meta/anilist/'.length));
      await handleSearch(query, requestUrl, response);
      return;
    }

    sendJson(response, 404, {
      error: 'route_not_found',
      message: `No scraper route is registered for ${path}.`
    });
  } catch (error) {
    log('error', 'request failed', {
      message: error.message,
      stack: logLevel === 'debug' ? error.stack : undefined
    });
    sendJson(response, 500, {
      error: 'scraper_request_failed',
      message: error.message
    });
  } finally {
    debug('request completed', {
      method: request.method,
      url: request.url,
      durationMs: Date.now() - startedAt
    });
  }
});

server.listen(port, host, () => {
  log('info', `SakugaVault scraper listening on http://${host}:${port}`, {
    defaultProvider: defaultProviderKey,
    providerSearchOrder: getProviderSearchOrder()
  });
});

async function handleTrending(requestUrl, response) {
  const page = readPositiveInt(requestUrl.searchParams.get('page'), 1);
  const perPage = readPositiveInt(requestUrl.searchParams.get('perPage'), 20);
  const cacheKey = `trending:${page}:${perPage}`;

  const payload = await cached(searchCache, cacheKey, async () => {
    const result = await withTimeout(createAnilist().fetchTrendingAnime(page, perPage), episodeInfoTimeoutMs, 'Timed out while fetching AniList trending titles.');
    return normalizeSearchResult(result);
  });

  sendJson(response, 200, payload);
}

async function handlePopular(requestUrl, response) {
  const page = readPositiveInt(requestUrl.searchParams.get('page'), 1);
  const perPage = readPositiveInt(requestUrl.searchParams.get('perPage'), 20);
  const cacheKey = `popular:${page}:${perPage}`;

  const payload = await cached(searchCache, cacheKey, async () => {
    const result = await withTimeout(createAnilist().fetchPopularAnime(page, perPage), episodeInfoTimeoutMs, 'Timed out while fetching AniList popular titles.');
    return normalizeSearchResult(result);
  });

  sendJson(response, 200, payload);
}

async function handleRecentEpisodes(requestUrl, response) {
  const page = readPositiveInt(requestUrl.searchParams.get('page'), 1);
  const perPage = readPositiveInt(requestUrl.searchParams.get('perPage'), 20);
  const cacheKey = `recent:${page}:${perPage}`;

  const payload = await cached(searchCache, cacheKey, async () => {
    const result = await withTimeout(createAnilist().fetchRecentEpisodes('Hianime', page, perPage), episodeInfoTimeoutMs, 'Timed out while fetching recent episodes.');
    return normalizeSearchResult(result);
  });

  sendJson(response, 200, payload);
}

async function handleSearch(query, requestUrl, response) {
  if (!query) {
    sendJson(response, 400, { error: 'missing_query', message: 'Search query is required.' });
    return;
  }

  const page = readPositiveInt(requestUrl.searchParams.get('page'), 1);
  const perPage = readPositiveInt(requestUrl.searchParams.get('perPage'), 20);
  const cacheKey = `search:${query}:${page}:${perPage}`;

  const payload = await cached(searchCache, cacheKey, async () => {
    const result = await withTimeout(createAnilist().search(query, page, perPage), episodeInfoTimeoutMs, `Timed out while searching AniList for "${query}".`);
    return normalizeSearchResult(result);
  });

  sendJson(response, 200, payload);
}

async function handleAnilistInfo(id, response) {
  if (!id) {
    sendJson(response, 400, { error: 'missing_id', message: 'AniList ID is required.' });
    return;
  }

  const payload = await cached(infoCache, `info:${id}`, async () => {
    const anilist = createAnilist();
    const metadata = await withTimeout(anilist.fetchAnilistInfoById(id), episodeInfoTimeoutMs, `Timed out while fetching AniList metadata for ${id}.`);
    const title = titleToString(metadata.title);
    const episodeBundle = await resolveEpisodeBundle(anilist, id, title);

    return normalizeAnimeInfo(metadata, episodeBundle.episodes, episodeBundle.providerKey);
  });

  sendJson(response, 200, payload);
}

async function handleWatch(requestUrl, response, fallbackProviderKey) {
  const taggedEpisodeId = requestUrl.searchParams.get('episodeId');
  if (!taggedEpisodeId) {
    sendJson(response, 400, { error: 'missing_episode_id', message: 'episodeId query parameter is required.' });
    return;
  }

  const preferredLanguage = normalizeLanguage(
    requestUrl.searchParams.get('preferredLanguage') || requestUrl.searchParams.get('language')
  );
  const languagePlan = buildLanguagePlan(
    preferredLanguage,
    requestUrl.searchParams.get('audioLanguage'),
    requestUrl.searchParams.get('subtitleLanguage')
  );
  const requestedTitle = (requestUrl.searchParams.get('title') || '').trim();
  const requestedEpisodeNumber = readPositiveInt(requestUrl.searchParams.get('episodeNumber'), 0);
  const allowRegionalFallback = allowRegionalLanguageFallbacks || readBoolean(requestUrl.searchParams.get('allowRegionalFallback'));
  const decoded = decodeEpisodeId(taggedEpisodeId, fallbackProviderKey);
  const provider = createAnimeProvider(decoded.providerKey);
  let sourceProviderKey = decoded.providerKey;
  let source;

  try {
    if (!canProviderServeLanguage(decoded.providerKey, languagePlan, false)) {
      throw new Error(`Provider ${decoded.providerKey} cannot satisfy requested audio/subtitle language preferences.`);
    }

    source = await fetchSourcesWithFallback(provider, decoded.providerKey, decoded.episodeId, languagePlan);
  } catch (error) {
    if (!requestedTitle || requestedEpisodeNumber <= 0) {
      throw error;
    }

    log('warn', 'Tagged provider source lookup failed; trying provider fallback.', {
      providerKey: decoded.providerKey,
      requestedTitle,
      requestedEpisodeNumber,
      preferredLanguage: languagePlan.preferredLanguage,
      audioLanguage: languagePlan.audioLanguage,
      subtitleLanguage: languagePlan.subtitleLanguage,
      message: error.message
    });

    let fallback = await findPlayableSourceByTitle(
      requestedTitle,
      requestedEpisodeNumber,
      languagePlan,
      [decoded.providerKey],
      { allowRegionalProviders: false }
    );

    if (!fallback && allowRegionalFallback) {
      log('warn', 'Language-safe providers failed; trying playable regional fallback.', {
        requestedTitle,
        requestedEpisodeNumber,
        preferredLanguage: languagePlan.preferredLanguage,
        audioLanguage: languagePlan.audioLanguage,
        subtitleLanguage: languagePlan.subtitleLanguage
      });

      fallback = await findPlayableSourceByTitle(
        requestedTitle,
        requestedEpisodeNumber,
        languagePlan,
        [],
        { allowRegionalProviders: true }
      );
    }

    if (!fallback) {
      sendJson(response, 404, {
        error: 'language_source_unavailable',
        message: buildLanguageUnavailableMessage(languagePlan),
        cause: error.message
      });
      return;
    }

    source = fallback.source;
    sourceProviderKey = fallback.providerKey;
  }

  sendJson(response, 200, normalizeSourceResponse(source, sourceProviderKey, languagePlan));
}

async function fetchSourcesWithFallback(provider, providerKey, episodeId, languagePlan = buildLanguagePlan('sub')) {
  if (normalizeProviderKey(providerKey) === 'animekai') {
    return await fetchAnimeKaiSources(provider, episodeId, languagePlan);
  }

  let lastError;

  for (const args of buildSourceAttempts(providerKey, languagePlan.preferredLanguage)) {
    try {
      const source = await withTimeout(
        provider.fetchEpisodeSources(episodeId, ...args),
        sourceTimeoutMs,
        `Timed out while fetching sources from ${providerKey}.`
      );

      if (Array.isArray(source.sources) && source.sources.some(item => item.url)) {
        return source;
      }

      lastError = new Error(`Provider ${providerKey} returned no playable sources.`);
    } catch (error) {
      lastError = error;
      debug('source server attempt failed', {
        providerKey,
        episodeId,
        args,
        message: error.message
      });
    }
  }

  throw lastError || new Error(`Provider ${providerKey} returned no playable sources.`);
}

async function fetchAnimeKaiSources(provider, episodeId, languagePlan) {
  let lastError;
  const languageAttempts = buildLanguageAttempts(languagePlan.preferredLanguage);

  for (const language of languageAttempts) {
    let servers;

    try {
      servers = await withTimeout(
        provider.fetchEpisodeServers(episodeId, language),
        sourceTimeoutMs,
        'Timed out while fetching AnimeKai episode servers.'
      );
    } catch (error) {
      lastError = error;
      debug('AnimeKai server lookup failed', { episodeId, language, message: error.message });
      continue;
    }

    for (const server of servers) {
      try {
        const iframeUrl = await resolveAnimeKaiIframeUrl(server.url);
        const source = await withTimeout(
          new MegaUp().extract(new URL(iframeUrl)),
          sourceTimeoutMs,
          'Timed out while extracting AnimeKai MegaUp source.'
        );

        if (!Array.isArray(source.sources) || !source.sources.some(item => item.url)) {
          lastError = new Error('AnimeKai returned no playable MegaUp sources.');
          continue;
        }

        if (!sourceSatisfiesSubtitleLanguage(source, languagePlan)) {
          lastError = new Error(`AnimeKai did not return ${languagePlan.subtitleLanguage} subtitles.`);
          continue;
        }

        source.sources = await filterReachableSources(source.sources);
        if (!Array.isArray(source.sources) || source.sources.length === 0) {
          lastError = new Error('AnimeKai returned source URLs, but none of them were reachable.');
          continue;
        }

        if (!await hasReachableRequestedSubtitle(source, languagePlan)) {
          lastError = new Error(`AnimeKai returned ${languagePlan.subtitleLanguage} subtitles, but the subtitle host was not reachable.`);
          continue;
        }

        return {
          ...source,
          headers: {
            Referer: iframeUrl,
            ...(source.headers || {})
          },
          sourceServer: server.name,
          intro: server.intro,
          outro: server.outro
        };
      } catch (error) {
        lastError = error;
        debug('AnimeKai MegaUp extraction failed', {
          episodeId,
          server: server.name,
          url: server.url,
          message: error.message
        });
      }
    }
  }

  throw lastError || new Error('AnimeKai returned no playable sources.');
}

function buildLanguageAttempts(preferredLanguage) {
  const language = normalizeLanguage(preferredLanguage) === 'dub' ? SubOrSub.DUB : SubOrSub.SUB;
  const fallbackLanguage = language === SubOrSub.DUB ? SubOrSub.SUB : null;
  return fallbackLanguage ? [language, fallbackLanguage] : [language];
}

async function resolveAnimeKaiIframeUrl(serverUrl) {
  const response = await fetch(serverUrl, {
    headers: {
      Connection: 'keep-alive',
      'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36'
    }
  });

  if (!response.ok) {
    throw new Error(`AnimeKai iframe request failed with status code ${response.status}.`);
  }

  const html = await response.text();
  const match = html.match(/<iframe[^>]+src=["']([^"']+)["']/i);
  if (!match?.[1]) {
    return serverUrl;
  }

  return new URL(match[1], serverUrl).href;
}

function sourceSatisfiesSubtitleLanguage(source, languagePlan) {
  if (languagePlan.subtitleLanguage === 'off') {
    return true;
  }

  const subtitles = Array.isArray(source.subtitles) ? source.subtitles : [];
  if (subtitles.length === 0) {
    return true;
  }

  return subtitles.some(track => normalizeLanguageCode(track.lang || track.label || track.language) === languagePlan.subtitleLanguage);
}

async function filterReachableSources(sources) {
  const reachableSources = [];

  for (const source of sources || []) {
    if (!source.url) {
      continue;
    }

    if (await isUrlReachable(source.url)) {
      reachableSources.push(source);
    }
  }

  return reachableSources;
}

async function hasReachableRequestedSubtitle(source, languagePlan) {
  if (languagePlan.subtitleLanguage === 'off') {
    return true;
  }

  const subtitles = Array.isArray(source.subtitles) ? source.subtitles : [];
  const requestedSubtitles = subtitles.filter(track =>
    normalizeLanguageCode(track.lang || track.label || track.language) === languagePlan.subtitleLanguage
  );

  if (requestedSubtitles.length === 0) {
    return true;
  }

  for (const subtitle of requestedSubtitles) {
    const subtitleUrl = subtitle.url || subtitle.file;
    if (subtitleUrl && await isUrlReachable(subtitleUrl)) {
      return true;
    }
  }

  return false;
}

async function isUrlReachable(url) {
  try {
    const response = await withTimeout(
      fetch(url, { method: 'GET', redirect: 'follow' }),
      providerFallbackTimeoutMs,
      `Timed out while probing ${url}.`
    );

    response.body?.cancel();
    return response.ok || response.status === 206;
  } catch (error) {
    debug('source URL probe failed', { url, message: error.cause?.code || error.message });
    return false;
  }
}

function buildSourceAttempts(providerKey, preferredLanguage) {
  const language = normalizeLanguage(preferredLanguage) === 'dub' ? SubOrSub.DUB : SubOrSub.SUB;
  const fallbackLanguage = language === SubOrSub.DUB ? SubOrSub.SUB : null;

  switch (normalizeProviderKey(providerKey)) {
    case 'animekai':
      return [
        [StreamingServers.MegaUp, language],
        ...(fallbackLanguage ? [[StreamingServers.MegaUp, fallbackLanguage]] : [])
      ];
    case 'hianime':
    case 'zoro':
      return [
        [StreamingServers.VidCloud, language],
        [StreamingServers.VidStreaming, language],
        [StreamingServers.StreamTape, language],
        [StreamingServers.StreamSB, language],
        ...(fallbackLanguage
          ? [
              [StreamingServers.VidCloud, fallbackLanguage],
              [StreamingServers.VidStreaming, fallbackLanguage],
              [StreamingServers.StreamTape, fallbackLanguage],
              [StreamingServers.StreamSB, fallbackLanguage]
            ]
          : [])
      ];
    default:
      return [
        ...(language === SubOrSub.SUB ? [[]] : []),
        [StreamingServers.VidCloud, language],
        [StreamingServers.MegaCloud, language],
        [StreamingServers.MegaUp, language],
        [StreamingServers.UpCloud, language],
        [StreamingServers.GogoCDN, language]
      ];
  }
}

function buildLanguagePlan(preferredLanguage = 'sub', audioLanguage, subtitleLanguage) {
  const normalizedPreferred = normalizeLanguage(preferredLanguage);
  const normalizedAudio = normalizeLanguageCode(audioLanguage) || (normalizedPreferred === 'dub' ? 'en' : 'ja');
  const normalizedSubtitle = normalizeSubtitleLanguage(subtitleLanguage, normalizedAudio);
  const effectivePreferred = normalizedAudio === 'en' ? 'dub' : 'sub';

  return {
    preferredLanguage: effectivePreferred,
    audioLanguage: normalizedAudio === 'en' ? 'en' : 'ja',
    subtitleLanguage: normalizedSubtitle
  };
}

function canProviderServeLanguage(providerKey, languagePlan, allowRegionalProviders = false) {
  const provider = normalizeProviderKey(providerKey);

  if (regionalHardSubProviders.has(provider)) {
    return allowRegionalProviders || allowRegionalLanguageFallbacks;
  }

  if (languagePlan.audioLanguage === 'en') {
    return ['animekai', 'hianime', 'zoro', 'kickassanime'].includes(provider);
  }

  if (languagePlan.subtitleLanguage === 'en') {
    return ['animekai', 'hianime', 'zoro', 'animepahe', 'kickassanime'].includes(provider);
  }

  return ['animekai', 'hianime', 'zoro', 'kickassanime'].includes(provider);
}

function buildLanguageUnavailableMessage(languagePlan) {
  const audioLabel = languagePlan.audioLanguage === 'en' ? 'English audio' : 'Japanese audio';
  const subtitleLabel = languagePlan.subtitleLanguage === 'off'
    ? 'subtitles off'
    : `${languagePlan.subtitleLanguage === 'ja' ? 'Japanese' : 'English'} subtitles`;

  return `No provider returned a playable source for ${audioLabel} with ${subtitleLabel}.`;
}

async function findPlayableSourceByTitle(title, episodeNumber, languagePlan, excludedProviderKeys, options = {}) {
  const allowRegionalProviders = Boolean(options.allowRegionalProviders);
  const excludedProviders = new Set(excludedProviderKeys.map(normalizeProviderKey));

  for (const providerKey of getProviderSearchOrder()) {
    if (excludedProviders.has(providerKey)) {
      continue;
    }

    if (!canProviderServeLanguage(providerKey, languagePlan, allowRegionalProviders)) {
      log('info', 'Skipping provider because it cannot satisfy requested language preferences.', {
        providerKey,
        audioLanguage: languagePlan.audioLanguage,
        subtitleLanguage: languagePlan.subtitleLanguage,
        allowRegionalProviders
      });
      continue;
    }

    try {
      const provider = createAnimeProvider(providerKey);
      const search = await withTimeout(
        provider.search(title, 1),
        providerFallbackTimeoutMs,
        `Timed out while searching ${providerKey}.`
      );
      const match = selectBestMatch(search.results || [], title);

      if (!match) {
        continue;
      }

      const info = await withTimeout(
        provider.fetchAnimeInfo(match.id),
        providerFallbackTimeoutMs,
        `Timed out while fetching ${providerKey} title info.`
      );
      const episode = selectEpisode(info.episodes || [], episodeNumber);

      if (!episode?.id) {
        continue;
      }

      const source = await fetchSourcesWithFallback(provider, providerKey, episode.id, languagePlan);
      return { providerKey, source };
    } catch (error) {
      log('warn', 'Provider source fallback failed.', {
        providerKey,
        title,
        episodeNumber,
        preferredLanguage: languagePlan.preferredLanguage,
        audioLanguage: languagePlan.audioLanguage,
        subtitleLanguage: languagePlan.subtitleLanguage,
        message: error.message
      });
    }
  }

  return null;
}

async function resolveEpisodeBundle(anilist, anilistId, title) {
  try {
    const episodes = await withTimeout(
      anilist.fetchEpisodesListById(anilistId),
      episodeInfoTimeoutMs,
      `Timed out while mapping AniList ID ${anilistId} to provider episodes.`
    );

    if (Array.isArray(episodes) && episodes.length > 0) {
      return {
        providerKey: defaultProviderKey,
        episodes: episodes.map(episode => normalizeEpisode(episode, defaultProviderKey))
      };
    }
  } catch (error) {
    log('warn', 'AniList episode mapping failed; trying direct provider search.', {
      anilistId,
      title,
      message: error.message
    });
  }

  return await findProviderEpisodes(title);
}

async function findProviderEpisodes(title, excludedProviderKeys = []) {
  const excludedProviders = new Set(excludedProviderKeys.map(normalizeProviderKey));

  for (const providerKey of getProviderSearchOrder()) {
    if (excludedProviders.has(providerKey)) {
      continue;
    }

    try {
      const provider = createAnimeProvider(providerKey);
      const search = await withTimeout(
        provider.search(title, 1),
        episodeInfoTimeoutMs,
        `Timed out while searching ${providerKey}.`
      );
      const match = selectBestMatch(search.results || [], title);

      if (!match) {
        continue;
      }

      const info = await withTimeout(
        provider.fetchAnimeInfo(match.id),
        episodeInfoTimeoutMs,
        `Timed out while fetching ${providerKey} title info.`
      );
      const episodes = Array.isArray(info.episodes) ? info.episodes : [];

      if (episodes.length > 0) {
        return {
          providerKey,
          episodes: episodes.map(episode => normalizeEpisode(episode, providerKey))
        };
      }
    } catch (error) {
      log('warn', 'Provider episode lookup failed.', {
        providerKey,
        title,
        message: error.message
      });
    }
  }

  return { providerKey: defaultProviderKey, episodes: [] };
}

function createAnilist() {
  return new META.Anilist(createAnimeProvider(defaultProviderKey));
}

function createAnimeProvider(providerKey) {
  const normalizedProvider = normalizeProviderKey(providerKey);
  const factory = providerFactories.get(normalizedProvider);

  if (!factory) {
    throw new Error(`Provider ${providerKey} is not installed in @consumet/extensions.`);
  }

  return factory();
}

function normalizeSearchResult(result) {
  return {
    currentPage: result.currentPage || 1,
    hasNextPage: Boolean(result.hasNextPage),
    totalPages: result.totalPages,
    totalResults: result.totalResults,
    results: Array.isArray(result.results)
      ? result.results.map(normalizeCatalogTitle).filter(item => item.id && item.image && titleToString(item.title))
      : []
  };
}

function normalizeCatalogTitle(item) {
  const totalEpisodes = readEpisodeCount(item);

  return {
    id: String(item.id || ''),
    malId: item.malId,
    title: item.title || '',
    image: item.image || item.cover || '',
    cover: item.cover || item.image || '',
    description: item.description || '',
    status: item.status,
    rating: item.rating,
    releaseDate: item.releaseDate,
    genres: Array.isArray(item.genres) ? item.genres : [],
    totalEpisodes,
    episodesCount: totalEpisodes,
    subOrDub: item.subOrDub || (item.hasDub ? 'both' : 'sub')
  };
}

function normalizeAnimeInfo(item, episodes, providerKey) {
  const totalEpisodes = readEpisodeCount(item) || episodes.length;

  return {
    id: String(item.id || ''),
    malId: item.malId,
    title: item.title || '',
    description: item.description || '',
    image: item.image || item.cover || '',
    cover: item.cover || item.image || '',
    status: item.status,
    rating: item.rating,
    releaseDate: item.releaseDate,
    genres: Array.isArray(item.genres) ? item.genres : [],
    totalEpisodes,
    episodesCount: totalEpisodes,
    subOrDub: determineLanguageAvailability(item, episodes),
    sourceProvider: providerKey,
    episodes
  };
}

function normalizeEpisode(episode, providerKey) {
  const number = Number.parseFloat(episode.number || episode.episodeNumber || '0') || 0;

  return {
    id: encodeEpisodeId(providerKey, String(episode.id || '')),
    number,
    title: episode.title || `Episode ${number}`,
    image: episode.image,
    description: episode.description,
    isSubbed: episode.isSubbed,
    isDubbed: episode.isDubbed
  };
}

function normalizeSourceResponse(source, providerKey, languagePlan) {
  const normalizedProvider = normalizeProviderKey(providerKey);
  const isRegionalHardSubProvider = regionalHardSubProviders.has(normalizedProvider);

  return {
    headers: source.headers || {},
    download: source.download,
    preferredLanguage: languagePlan.preferredLanguage,
    audioLanguage: isRegionalHardSubProvider ? null : languagePlan.audioLanguage,
    subtitleLanguage: isRegionalHardSubProvider ? null : languagePlan.subtitleLanguage,
    languageSource: isRegionalHardSubProvider ? 'hardcoded' : 'provider',
    languageWarning: isRegionalHardSubProvider
      ? `Fell back to ${providerKey}; this provider may return hardcoded regional subtitles because English/Japanese providers did not return a playable stream.`
      : undefined,
    subtitles: Array.isArray(source.subtitles)
      ? source.subtitles.map(normalizeSubtitleTrack).filter(Boolean)
      : [],
    sources: Array.isArray(source.sources)
      ? source.sources.map(item => ({
          url: item.url,
          quality: item.quality,
          isM3U8: Boolean(item.isM3U8),
          isDASH: Boolean(item.isDASH),
          size: item.size,
          server: item.server || providerKey
        })).filter(item => item.url)
      : []
  };
}

function normalizeSubtitleTrack(track) {
  const url = track.url || track.file;
  if (!url) {
    return null;
  }

  const label = String(track.lang || track.label || track.language || '').trim();
  const language = normalizeLanguageCode(label);

  return {
    url,
    language: language || 'unknown',
    label: label || language || 'Subtitle',
    kind: track.kind || 'subtitles'
  };
}

function encodeEpisodeId(providerKey, episodeId) {
  return `sv1:${normalizeProviderKey(providerKey)}:${Buffer.from(episodeId, 'utf8').toString('base64url')}`;
}

function decodeEpisodeId(value, fallbackProviderKey) {
  if (!value.startsWith('sv1:')) {
    return {
      providerKey: normalizeProviderKey(fallbackProviderKey),
      episodeId: value
    };
  }

  const parts = value.split(':');
  if (parts.length < 3) {
    throw new Error('Tagged episode ID is malformed.');
  }

  return {
    providerKey: normalizeProviderKey(parts[1]),
    episodeId: Buffer.from(parts.slice(2).join(':'), 'base64url').toString('utf8')
  };
}

function selectBestMatch(results, requestedTitle) {
  const normalizedRequested = normalizeText(requestedTitle);

  const ranked = results
    .map(result => ({
      result,
      score: scoreTitle(normalizedRequested, normalizeText(titleToString(result.title)))
    }))
    .filter(entry => entry.result.id)
    .sort((left, right) => right.score - left.score)
    .map(entry => entry);
  const matched = ranked.find(entry => entry.score > 0);

  return (matched || ranked[0])?.result;
}

function selectEpisode(episodes, episodeNumber) {
  const exact = episodes.find(episode => {
    const number = Number.parseFloat(episode.number || episode.episodeNumber || '0');
    return Number.isFinite(number) && Math.round(number) === episodeNumber;
  });

  return exact || episodes[episodeNumber - 1];
}

function determineLanguageAvailability(item, episodes) {
  const explicit = item.subOrDub || (item.hasSub && item.hasDub
    ? 'both'
    : item.hasDub
      ? 'dub'
      : item.hasSub
        ? 'sub'
        : null);
  const hasSubEpisode = episodes.some(episode => episode.isSubbed === true);
  const hasDubEpisode = episodes.some(episode => episode.isDubbed === true);

  if (hasSubEpisode && hasDubEpisode) {
    return 'both';
  }

  if (hasDubEpisode) {
    return 'dub';
  }

  if (hasSubEpisode) {
    return 'sub';
  }

  return explicit || 'sub';
}

function scoreTitle(requested, candidate) {
  if (!requested || !candidate) {
    return 0;
  }

  if (requested === candidate) {
    return 1000;
  }

  if (candidate.startsWith(requested) || requested.startsWith(candidate)) {
    return 800;
  }

  if (candidate.includes(requested) || requested.includes(candidate)) {
    return 600;
  }

  const requestedTokens = requested.split(' ').filter(Boolean);
  const candidateTokens = new Set(candidate.split(' ').filter(Boolean));

  return requestedTokens.filter(token => candidateTokens.has(token)).length;
}

function titleToString(title) {
  if (!title) {
    return '';
  }

  if (typeof title === 'string') {
    return title;
  }

  return title.english || title.userPreferred || title.romaji || title.native || '';
}

function normalizeText(value) {
  return String(value || '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()
    .replace(/\s+/g, ' ');
}

function readEpisodeCount(item) {
  const value = item.totalEpisodes || item.episodesCount || item.episodes || item.episodeNumber;
  const parsed = Number.parseInt(value, 10);

  return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
}

function normalizeProviderKey(provider) {
  return String(provider || '').trim().toLowerCase().replace(/^anime\//, '');
}

function normalizeLanguage(language) {
  return String(language || '').trim().toLowerCase() === 'dub' ? 'dub' : 'sub';
}

function normalizeSubtitleLanguage(language, audioLanguage = 'ja') {
  const normalized = normalizeLanguageCode(language);
  if (['en', 'ja', 'off'].includes(normalized)) {
    return normalized;
  }

  return audioLanguage === 'ja' ? 'en' : 'off';
}

function normalizeLanguageCode(language) {
  const normalized = String(language || '').trim().toLowerCase();
  if (!normalized) {
    return '';
  }

  const compact = normalized
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[_\s]+/g, '-');
  const baseLanguage = compact.split('-')[0];

  if (compact.includes('english')) {
    return 'en';
  }

  if (compact.includes('japanese')) {
    return 'ja';
  }

  if (compact.includes('spanish') || compact.includes('espanol')) {
    return 'es';
  }

  if (compact.includes('german')) {
    return 'de';
  }

  switch (baseLanguage) {
    case 'eng':
    case 'english':
      return 'en';
    case 'jpn':
    case 'jp':
    case 'japanese':
      return 'ja';
    case 'spa':
    case 'es':
    case 'espanol':
      return 'es';
    case 'ger':
    case 'deu':
    case 'de':
    case 'german':
      return 'de';
    case 'none':
    case 'false':
    case 'disabled':
      return 'off';
    default:
      return baseLanguage;
  }
}

function getProviderSearchOrder() {
  const configured = providerSearchOrder.length > 0
    ? providerSearchOrder
    : [defaultProviderKey, ...installedProviderKeys.filter(providerKey => providerKey !== defaultProviderKey)];

  return [...new Set(configured.filter(providerKey => providerFactories.has(providerKey)))];
}

function parseCsv(value) {
  return String(value || '')
    .split(',')
    .map(item => item.trim())
    .filter(Boolean);
}

function readPositiveInt(value, fallback) {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function readBoolean(value) {
  return ['1', 'true', 'yes', 'on'].includes(String(value || '').trim().toLowerCase());
}

function decodePathValue(value) {
  return decodeURIComponent(String(value || '').replace(/\+/g, '%20')).trim();
}

async function cached(cache, key, factory) {
  const cachedValue = cache.get(key);
  if (cachedValue && cachedValue.expiresAt > Date.now()) {
    return cachedValue.value;
  }

  const value = await factory();
  cache.set(key, {
    value,
    expiresAt: Date.now() + cacheTtlMs
  });

  return value;
}

function withTimeout(promise, timeoutMs, message) {
  let timeout;

  return Promise.race([
    promise,
    new Promise((_, reject) => {
      timeout = setTimeout(() => reject(new Error(message)), timeoutMs);
    })
  ]).finally(() => clearTimeout(timeout));
}

function sendJson(response, statusCode, payload) {
  const body = JSON.stringify(payload);
  response.writeHead(statusCode, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': Buffer.byteLength(body),
    'Cache-Control': 'no-store'
  });
  response.end(body);
}

function debug(message, data) {
  if (logLevel === 'debug') {
    log('debug', message, data);
  }
}

function log(level, message, data = {}) {
  const payload = {
    level,
    time: new Date().toISOString(),
    message,
    ...Object.fromEntries(Object.entries(data).filter(([, value]) => value !== undefined))
  };

  console[level === 'error' ? 'error' : 'log'](JSON.stringify(payload));
}
