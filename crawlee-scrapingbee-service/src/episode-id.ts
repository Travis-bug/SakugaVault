/**
 * Episode-id tagging, compatible with scraper-service's sv1 scheme.
 *
 * The C# StreamScraperService takes the `id` we return from /info and passes it
 * straight back as ?episodeId= on /watch. We encode the absolute target URL of
 * the episode page so /watch can re-open it without another search round-trip.
 *
 *   sv1:<provider>:<base64url(payload)>
 *
 * Search-result ids use a "cs:" prefix so /info can tell a real (resolvable)
 * id from a bare AniList numeric id — for the latter we 404 and let the C#
 * layer fall through to the /meta/anilist/{title} search path.
 */

import { Buffer } from 'node:buffer';

const SEARCH_ID_PREFIX = 'cs:';

export function encodeSearchId(animePageUrl: string): string {
  return SEARCH_ID_PREFIX + Buffer.from(animePageUrl, 'utf8').toString('base64url');
}

export function isResolvableSearchId(id: string): boolean {
  return id.startsWith(SEARCH_ID_PREFIX);
}

export function decodeSearchId(id: string): string {
  if (!isResolvableSearchId(id)) {
    throw new Error('Not a resolvable crawlee search id.');
  }
  return Buffer.from(id.slice(SEARCH_ID_PREFIX.length), 'base64url').toString('utf8');
}

export function encodeEpisodeId(provider: string, episodePageUrl: string): string {
  return `sv1:${provider}:${Buffer.from(episodePageUrl, 'utf8').toString('base64url')}`;
}

export function decodeEpisodeId(taggedId: string): { provider: string; episodePageUrl: string } {
  if (!taggedId.startsWith('sv1:')) {
    // Untagged ids are assumed to already be a direct episode URL.
    return { provider: 'crawlee_scrapingbee', episodePageUrl: taggedId };
  }
  const parts = taggedId.split(':');
  if (parts.length < 3) {
    throw new Error('Tagged episode id is malformed.');
  }
  return {
    provider: parts[1] ?? 'crawlee_scrapingbee',
    episodePageUrl: Buffer.from(parts.slice(2).join(':'), 'base64url').toString('utf8')
  };
}
