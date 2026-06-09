/**
 * Wire types for the small Consumet-compatible surface that SakugaVault's
 * StreamScraperService fans out to. These mirror exactly what the C# layer
 * deserializes (see StreamScraperService.ConsumetWatchResponse /
 * ResolverInfoResponse / ResolverSearchResponse), so this resolver can be
 * registered as just another entry in Scrapers__PlaybackResolvers.
 */

/** One episode as returned by /meta/anilist/info/{id}. */
export interface ResolverEpisode {
  /** Tagged episode id (sv1:<provider>:<base64url>) that round-trips to /watch. */
  id: string;
  number: number;
  title: string;
}

/** Body of /meta/anilist/info/{id}. */
export interface ResolverInfoResponse {
  sourceProvider: string;
  episodes: ResolverEpisode[];
}

/** One search hit as returned by /meta/anilist/{query}. */
export interface ResolverSearchTitle {
  /** Opaque id this service can later resolve back into an info page. */
  id: string;
  title: string;
}

/** Body of /meta/anilist/{query}. */
export interface ResolverSearchResponse {
  results: ResolverSearchTitle[];
}

/** A single playable source line. Field names match ConsumetSourceResponse. */
export interface ResolverSource {
  url: string;
  quality?: string;
  isM3U8: boolean;
  server?: string;
}

/** A single subtitle track. Field names match ConsumetSubtitleResponse. */
export interface ResolverSubtitle {
  url: string;
  language: string;
  label: string;
  kind: string;
}

/**
 * Body of /anime/{provider}/watch.
 *
 * languageSource === 'hardcoded' is the contract signal the C# pipeline uses
 * to gate a candidate behind allowRegionalFallback and apply the -1000 ranker
 * penalty. We set it whenever the source carries unverified / non-EN-JA
 * (e.g. Spanish, Portuguese, multi-sub) language tracks — this is where the
 * old spec's "subLanguage: 'invalid'" maps without bypassing the ranker.
 */
export interface ResolverWatchResponse {
  headers: Record<string, string>;
  sources: ResolverSource[];
  audioLanguage: string | null;
  subtitleLanguage: string | null;
  languageSource: 'provider' | 'hardcoded';
  languageWarning?: string;
  subtitles: ResolverSubtitle[];
}

/** Normalized language codes used internally. */
export type LanguageCode = 'en' | 'ja' | 'off' | 'es' | 'pt' | string;

/** Parsed audio/subtitle preference for a single resolve. */
export interface LanguagePlan {
  preferredLanguage: 'sub' | 'dub';
  audioLanguage: 'en' | 'ja';
  subtitleLanguage: LanguageCode;
}
