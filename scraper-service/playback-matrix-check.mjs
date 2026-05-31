#!/usr/bin/env node

/**
 * Exercises playback resolution through the SakugaVault API.
 *
 * Required environment:
 *   MATRIX_IDENTIFIER
 *   MATRIX_PASSWORD
 *
 * Optional environment:
 *   API_BASE_URL=http://localhost:8080
 *   MATRIX_TITLES=Bleach,Naruto,...
 */

const apiBaseUrl = String(process.env.API_BASE_URL || 'http://localhost:8080').replace(/\/+$/, '');
const identifier = String(process.env.MATRIX_IDENTIFIER || '').trim();
const password = String(process.env.MATRIX_PASSWORD || '');
const titles = parseCsv(process.env.MATRIX_TITLES || [
  'Bleach',
  'Naruto',
  'One Piece',
  'Attack on Titan',
  'Demon Slayer',
  'Jujutsu Kaisen',
  'My Hero Academia',
  'Vinland Saga',
  'Frieren',
  'Death Note'
].join(','));
const episodes = [1, 2, 3];

if (!identifier || !password) {
  console.error('MATRIX_IDENTIFIER and MATRIX_PASSWORD are required.');
  process.exit(1);
}

const session = await request('/api/auth/login', {
  method: 'POST',
  body: {
    identifier,
    password
  }
});
const accessToken = session.accessToken;
const results = [];

for (const requestedTitle of titles) {
  const search = await request(`/api/catalog/search?q=${encodeURIComponent(requestedTitle)}&limit=5`, {
    accessToken
  });
  const title = selectBestMatch(search.results || [], requestedTitle);

  if (!title) {
    for (const episodeNumber of episodes) {
      results.push({
        requestedTitle,
        resolvedTitle: null,
        episodeNumber,
        ok: false,
        reason: 'No catalog search result.'
      });
    }

    console.log(`[matrix] ${requestedTitle}: no catalog search result`);
    continue;
  }

  for (const episodeNumber of episodes) {
    const startedAt = Date.now();

    try {
      const playback = await request(`/api/watch/${title.id}/resolve-playback`, {
        method: 'POST',
        accessToken,
        timeoutMs: 60000,
        body: {
          episodeNumber,
          preferredLanguage: 'sub',
          audioLanguage: 'ja',
          subtitleLanguage: 'en',
          allowRegionalFallback: false
        }
      });
      const result = {
        requestedTitle,
        resolvedTitle: title.title,
        episodeNumber,
        ok: Boolean(playback.isResolved && playback.streamUrl),
        resolver: playback.resolver,
        sourceHost: playback.sourceHost,
        audioLanguage: playback.audioLanguage,
        subtitleLanguage: playback.subtitleLanguage,
        protocol: playback.preferredProtocol,
        statusMessage: playback.statusMessage,
        durationMs: Date.now() - startedAt
      };
      results.push(result);
      console.log(`[matrix] ${requestedTitle} episode ${episodeNumber}: ${result.ok ? 'resolved' : 'unresolved'} (${result.durationMs}ms)`);
    } catch (error) {
      results.push({
        requestedTitle,
        resolvedTitle: title.title,
        episodeNumber,
        ok: false,
        reason: error.message,
        durationMs: Date.now() - startedAt
      });
      console.log(`[matrix] ${requestedTitle} episode ${episodeNumber}: request failed (${error.message})`);
    }
  }
}

const resolved = results.filter(result => result.ok).length;
console.log(JSON.stringify({
  summary: {
    titles: titles.length,
    episodesPerTitle: episodes.length,
    requests: results.length,
    resolved,
    unresolved: results.length - resolved
  },
  results
}, null, 2));

function selectBestMatch(results, requestedTitle) {
  const requested = normalizeTitle(requestedTitle);
  return [...results]
    .sort((left, right) => {
      const leftTitle = normalizeTitle(left.title);
      const rightTitle = normalizeTitle(right.title);
      return scoreTitle(requested, rightTitle) - scoreTitle(requested, leftTitle) ||
        Math.abs(leftTitle.length - requested.length) - Math.abs(rightTitle.length - requested.length);
    })
    .at(0);
}

function scoreTitle(requested, candidate) {
  if (candidate === requested) {
    return 1000;
  }

  if (candidate.includes(requested) || requested.includes(candidate)) {
    return 500;
  }

  const requestedTokens = requested.split(' ').filter(Boolean);
  const candidateTokens = new Set(candidate.split(' ').filter(Boolean));
  return requestedTokens.filter(token => candidateTokens.has(token)).length;
}

function normalizeTitle(value) {
  return String(value || '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim()
    .replace(/\s+/g, ' ');
}

function parseCsv(value) {
  return String(value || '')
    .split(',')
    .map(item => item.trim())
    .filter(Boolean);
}

async function request(path, options = {}) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), options.timeoutMs || 30000);

  try {
    const response = await fetch(`${apiBaseUrl}${path}`, {
      method: options.method || 'GET',
      headers: {
        ...(options.accessToken ? { Authorization: `Bearer ${options.accessToken}` } : {}),
        ...(options.body ? { 'Content-Type': 'application/json' } : {})
      },
      body: options.body ? JSON.stringify(options.body) : undefined,
      signal: controller.signal
    });
    const text = await response.text();
    const body = text ? JSON.parse(text) : null;

    if (!response.ok) {
      throw new Error(`${response.status} ${body?.detail || body?.message || response.statusText}`);
    }

    return body;
  } catch (error) {
    if (error.name === 'AbortError') {
      throw new Error(`Timed out after ${options.timeoutMs || 30000}ms.`);
    }

    throw error;
  } finally {
    clearTimeout(timeout);
  }
}
