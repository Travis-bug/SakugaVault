/**
 * Language normalization + the foreign-language interceptor.
 *
 * This mirrors the normalization in scraper-service/server.js and the C#
 * StreamScraperService so candidates from this resolver rank consistently
 * alongside the Consumet resolver. The old spec's "subLanguage: 'invalid'"
 * idea is implemented here as classifyForeign(): anything that is hardcoded
 * non-EN/JA (Spanish, Portuguese, multi-sub, ...) is flagged so the watch
 * handler can emit languageSource: 'hardcoded' instead of silently serving it.
 */

import type { LanguageCode, LanguagePlan } from './types.ts';

export function normalizeLanguageCode(value: string | null | undefined): LanguageCode {
  const normalized = String(value ?? '').trim().toLowerCase();
  if (!normalized) {
    return '';
  }

  const compact = normalized
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[_\s]+/g, '-');

  if (compact.includes('english')) return 'en';
  if (compact.includes('japanese')) return 'ja';
  if (compact.includes('spanish') || compact.includes('espanol') || compact.includes('castellano')) return 'es';
  if (compact.includes('portuguese') || compact.includes('portugues')) return 'pt';

  const base = compact.split('-')[0] ?? '';
  switch (base) {
    case 'eng':
    case 'english':
      return 'en';
    case 'jpn':
    case 'jp':
    case 'japanese':
      return 'ja';
    case 'spa':
    case 'es':
    case 'esp':
      return 'es';
    case 'por':
    case 'pt':
    case 'ptbr':
      return 'pt';
    case 'none':
    case 'false':
    case 'disabled':
      return 'off';
    default:
      return base;
  }
}

export function normalizeSubtitleLanguage(value: string | null | undefined, audioLanguage: 'en' | 'ja' = 'ja'): LanguageCode {
  const normalized = normalizeLanguageCode(value);
  if (['en', 'ja', 'off'].includes(normalized)) {
    return normalized;
  }
  // Unknown/foreign requested subtitle: default to the sensible track for the audio.
  return audioLanguage === 'ja' ? 'en' : 'off';
}

export function buildLanguagePlan(
  preferredLanguage: string | null | undefined,
  audioLanguage: string | null | undefined,
  subtitleLanguage: string | null | undefined
): LanguagePlan {
  const normalizedAudio = normalizeLanguageCode(audioLanguage);
  const audio: 'en' | 'ja' = normalizedAudio === 'en'
    ? 'en'
    : normalizedAudio === 'ja'
      ? 'ja'
      : String(preferredLanguage ?? '').trim().toLowerCase() === 'dub'
        ? 'en'
        : 'ja';

  return {
    preferredLanguage: audio === 'en' ? 'dub' : 'sub',
    audioLanguage: audio,
    subtitleLanguage: normalizeSubtitleLanguage(subtitleLanguage, audio)
  };
}

/** True for codes we treat as unverified/foreign for ranking purposes. */
export function isForeignLanguage(code: LanguageCode): boolean {
  return code !== '' && code !== 'en' && code !== 'ja' && code !== 'off';
}

const FOREIGN_MARKERS = [
  'spanish', 'espanol', 'castellano', 'latino',
  'portuguese', 'portugues', 'legendado', 'dublado',
  'french', 'francais', 'german', 'deutsch',
  'italian', 'italiano', 'multi-sub', 'multi sub', 'multisub', 'multi-audio'
];

/**
 * Scans arbitrary page/track text for hardcoded foreign-language markers.
 * Returns a warning string when the source looks non-EN/JA, otherwise null.
 */
export function classifyForeign(text: string | null | undefined): string | null {
  const haystack = String(text ?? '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase();

  const hit = FOREIGN_MARKERS.find((marker) => haystack.includes(marker));
  if (!hit) {
    return null;
  }
  return `Source appears to carry hardcoded "${hit}" language tracks rather than verified English/Japanese.`;
}
