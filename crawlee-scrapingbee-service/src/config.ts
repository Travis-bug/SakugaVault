/**
 * Environment-driven configuration.
 *
 * SECURITY / TRUST BOUNDARY:
 *   SCRAPINGBEE_API_KEY is read here and used only inside this server-side
 *   container. It is never returned in any HTTP response and never reaches the
 *   browser or the C# API. This service has no host port mapping in
 *   docker-compose; it is reachable only on the internal Docker network by the
 *   SakugaVault API. Do not add CORS or a public route.
 *
 * The target-site selectors are configuration on purpose: a JS-heavy source is
 * chosen per deployment, so its DOM shape lives in env, not in code.
 */

/** Required env vars that were missing at load time. */
const missingRequired: string[] = [];

function required(name: string): string {
  const value = process.env[name];
  if (!value || value.trim().length === 0) {
    // Do not throw: the container must boot so /health works and the C# fan-out
    // can treat an unconfigured resolver as a failed candidate (clean 503),
    // rather than crash-looping when the resolver is left disabled.
    missingRequired.push(name);
    return '';
  }
  return value.trim();
}

function optional(name: string, fallback: string): string {
  const value = process.env[name];
  return value && value.trim().length > 0 ? value.trim() : fallback;
}

function int(name: string, fallback: number): number {
  const parsed = Number.parseInt(process.env[name] ?? '', 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function bool(name: string, fallback: boolean): boolean {
  const raw = process.env[name];
  if (raw === undefined) {
    return fallback;
  }
  return ['1', 'true', 'yes', 'on'].includes(raw.trim().toLowerCase());
}

export interface ScrapingBeeConfig {
  apiKey: string;
  endpoint: string;
  renderJs: boolean;
  premiumProxy: boolean;
  stealthProxy: boolean;
  countryCode: string;
  waitMs: number;
  blockResources: boolean;
  timeoutMs: number;
}

export interface TargetConfig {
  /** Stable name reported as sourceProvider and used in the sv1: tag. */
  providerName: string;
  baseUrl: string;
  /** Search URL path; {query} is replaced with the url-encoded title. */
  searchPath: string;
  searchItemSelector: string;
  searchLinkSelector: string;
  searchTitleSelector: string;
  /** Episode list on an anime page. */
  episodeItemSelector: string;
  episodeLinkSelector: string;
  episodeNumberAttr: string;
  episodeTitleSelector: string;
  /** Optional iframe to follow on an episode page before extracting streams. */
  episodeIframeSelector: string;
  /** Regex (with a capture group) that pulls the stream URL from rendered HTML/JS. */
  streamUrlRegex: RegExp;
  /** Optional regex (capture group) for subtitle track URLs. */
  subtitleUrlRegex: RegExp | null;
}

export interface ServiceConfig {
  port: number;
  host: string;
  cacheTtlMs: number;
  /** True only when every required env var is present. */
  configured: boolean;
  /** Names of required env vars that are missing (empty when configured). */
  missing: string[];
  scrapingBee: ScrapingBeeConfig;
  target: TargetConfig;
}

function compileRegex(name: string, fallback: string): RegExp {
  const pattern = optional(name, fallback);
  try {
    return new RegExp(pattern, 'i');
  } catch (error) {
    throw new Error(`Environment variable ${name} is not a valid regular expression: ${(error as Error).message}`);
  }
}

export function loadConfig(): ServiceConfig {
  const subtitlePattern = optional('TARGET_SUBTITLE_URL_REGEX', '');
  missingRequired.length = 0;

  const config: ServiceConfig = {
    port: int('PORT', 3200),
    host: optional('HOST', '0.0.0.0'),
    cacheTtlMs: int('CACHE_TTL_MS', 300_000),
    configured: false,
    missing: [] as string[],
    scrapingBee: {
      apiKey: required('SCRAPINGBEE_API_KEY'),
      endpoint: optional('SCRAPINGBEE_ENDPOINT', 'https://app.scrapingbee.com/api/v1/'),
      renderJs: bool('SCRAPINGBEE_RENDER_JS', true),
      premiumProxy: bool('SCRAPINGBEE_PREMIUM_PROXY', true),
      stealthProxy: bool('SCRAPINGBEE_STEALTH_PROXY', false),
      countryCode: optional('SCRAPINGBEE_COUNTRY_CODE', 'us'),
      waitMs: int('SCRAPINGBEE_WAIT_MS', 3_000),
      blockResources: bool('SCRAPINGBEE_BLOCK_RESOURCES', false),
      timeoutMs: int('SCRAPINGBEE_TIMEOUT_MS', 40_000)
    },
    target: {
      providerName: optional('TARGET_PROVIDER_NAME', 'crawlee_scrapingbee'),
      baseUrl: required('TARGET_BASE_URL').replace(/\/+$/, ''),
      searchPath: optional('TARGET_SEARCH_PATH', '/search?keyword={query}'),
      searchItemSelector: optional('TARGET_SEARCH_ITEM_SELECTOR', '.film_list-wrap .flw-item'),
      searchLinkSelector: optional('TARGET_SEARCH_LINK_SELECTOR', 'a.film-poster-ahref, a.dynamic-name'),
      searchTitleSelector: optional('TARGET_SEARCH_TITLE_SELECTOR', '.film-name a, .dynamic-name'),
      episodeItemSelector: optional('TARGET_EPISODE_ITEM_SELECTOR', '.ss-list a.ssl-item, .episodes-ul a'),
      episodeLinkSelector: optional('TARGET_EPISODE_LINK_SELECTOR', 'self'),
      episodeNumberAttr: optional('TARGET_EPISODE_NUMBER_ATTR', 'data-number'),
      episodeTitleSelector: optional('TARGET_EPISODE_TITLE_SELECTOR', 'self'),
      episodeIframeSelector: optional('TARGET_EPISODE_IFRAME_SELECTOR', 'iframe'),
      streamUrlRegex: compileRegex('TARGET_STREAM_URL_REGEX', '(https?:\\/\\/[^"\\\']+\\.m3u8[^"\\\']*)'),
      subtitleUrlRegex: subtitlePattern ? compileRegex('TARGET_SUBTITLE_URL_REGEX', subtitlePattern) : null
    }
  };

  config.missing = [...missingRequired];
  config.configured = config.missing.length === 0;
  return config;
}
