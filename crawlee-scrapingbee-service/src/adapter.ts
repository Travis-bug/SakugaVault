/**
 * Target-site adapter.
 *
 * Turns a JS-heavy streaming site into the three operations the resolver needs:
 *   search(title)        -> candidate titles
 *   listEpisodes(url)    -> episodes for a title
 *   resolveStream(url)   -> playable sources + subtitles for one episode
 *
 * The site's DOM shape and stream-URL pattern are configuration (config.ts),
 * because the JS-heavy source is chosen per deployment. The crawling and
 * normalization logic below is generic and complete; only the selectors/regex
 * need tuning for a given target.
 */

import * as cheerio from 'cheerio';
import type { TargetConfig } from './config.ts';
import { ScrapingBeeClient } from './scrapingbee.ts';
import { encodeEpisodeId, encodeSearchId } from './episode-id.ts';
import { classifyForeign, normalizeLanguageCode } from './language.ts';
import type {
  LanguagePlan,
  ResolverEpisode,
  ResolverSearchTitle,
  ResolverSource,
  ResolverSubtitle,
  ResolverWatchResponse
} from './types.ts';

export class TargetAdapter {
  constructor(
    private readonly config: TargetConfig,
    private readonly client: ScrapingBeeClient
  ) {}

  get providerName(): string {
    return this.config.providerName;
  }

  private absolute(href: string): string {
    return new URL(href, this.config.baseUrl).href;
  }

  async search(title: string): Promise<ResolverSearchTitle[]> {
    const searchUrl = this.config.baseUrl + this.config.searchPath.replace('{query}', encodeURIComponent(title));
    // Search pages are usually static; skip JS rendering to save credits.
    const html = await this.client.fetchHtml(searchUrl, { render: false });
    const $ = cheerio.load(html);

    const results: ResolverSearchTitle[] = [];
    $(this.config.searchItemSelector).each((_, element) => {
      const item = $(element);
      const link = this.config.searchLinkSelector === 'self'
        ? item
        : item.find(this.config.searchLinkSelector).first();
      const href = link.attr('href');
      if (!href) {
        return;
      }
      const titleNode = this.config.searchTitleSelector === 'self'
        ? link
        : item.find(this.config.searchTitleSelector).first();
      const text = (titleNode.attr('title') || titleNode.text() || '').trim();
      if (!text) {
        return;
      }
      results.push({ id: encodeSearchId(this.absolute(href)), title: text });
    });

    return results;
  }

  async listEpisodes(animePageUrl: string): Promise<ResolverEpisode[]> {
    // Episode lists are often injected client-side; use the configured default.
    const html = await this.client.fetchHtml(animePageUrl);
    const $ = cheerio.load(html);

    const episodes: ResolverEpisode[] = [];
    $(this.config.episodeItemSelector).each((index, element) => {
      const item = $(element);
      const link = this.config.episodeLinkSelector === 'self'
        ? item
        : item.find(this.config.episodeLinkSelector).first();
      const href = link.attr('href');
      if (!href) {
        return;
      }

      const numberRaw = item.attr(this.config.episodeNumberAttr)
        ?? link.attr(this.config.episodeNumberAttr)
        ?? String(index + 1);
      const number = Number.parseFloat(numberRaw) || index + 1;

      const titleNode = this.config.episodeTitleSelector === 'self'
        ? link
        : item.find(this.config.episodeTitleSelector).first();
      const title = (titleNode.attr('title') || titleNode.text() || `Episode ${number}`).trim();

      episodes.push({
        id: encodeEpisodeId(this.config.providerName, this.absolute(href)),
        number,
        title
      });
    });

    // De-duplicate by episode number, keeping the first occurrence.
    const seen = new Set<number>();
    return episodes.filter((episode) => {
      if (seen.has(episode.number)) {
        return false;
      }
      seen.add(episode.number);
      return true;
    });
  }

  async resolveStream(episodePageUrl: string, plan: LanguagePlan): Promise<ResolverWatchResponse> {
    // 1. Render the episode page so the player initializes.
    const pageHtml = await this.client.fetchHtml(episodePageUrl, { render: true });
    let extractionHtml = pageHtml;
    let referer = episodePageUrl;

    // 2. Optionally follow the embedded player iframe and render that too.
    if (this.config.episodeIframeSelector) {
      const $page = cheerio.load(pageHtml);
      const iframeSrc = $page(this.config.episodeIframeSelector).first().attr('src');
      if (iframeSrc) {
        const iframeUrl = this.absolute(iframeSrc);
        extractionHtml = await this.client.fetchHtml(iframeUrl, { render: true });
        referer = iframeUrl;
      }
    }

    // 3. Extract the stream URL(s) from rendered HTML/JS.
    const streamUrls = this.matchAll(extractionHtml, this.config.streamUrlRegex);
    const sources: ResolverSource[] = streamUrls.map((url) => ({
      url,
      quality: this.guessQuality(url),
      isM3U8: /\.m3u8(?:$|\?)/i.test(url),
      server: this.config.providerName
    }));

    // 4. Extract subtitle tracks, if a pattern is configured.
    const subtitles: ResolverSubtitle[] = this.config.subtitleUrlRegex
      ? this.matchAll(extractionHtml, this.config.subtitleUrlRegex).map((url) => {
          const language = normalizeLanguageCode(url);
          return {
            url,
            language: language || 'unknown',
            label: language ? language.toUpperCase() : 'Subtitle',
            kind: 'subtitles'
          };
        })
      : [];

    // 5. Foreign-language interceptor: if the page or any subtitle track looks
    //    like hardcoded non-EN/JA content, flag it as 'hardcoded' so the C#
    //    ranker gates/penalizes it (the old spec's 'invalid' path, done safely).
    const foreignWarning = classifyForeign(extractionHtml)
      ?? subtitles.map((track) => classifyForeign(track.label)).find(Boolean)
      ?? null;
    const isHardcoded = foreignWarning !== null;

    return {
      headers: { Referer: referer },
      sources,
      audioLanguage: isHardcoded ? null : plan.audioLanguage,
      subtitleLanguage: isHardcoded ? null : plan.subtitleLanguage,
      languageSource: isHardcoded ? 'hardcoded' : 'provider',
      languageWarning: foreignWarning ?? undefined,
      subtitles
    };
  }

  private matchAll(html: string, regex: RegExp): string[] {
    const global = new RegExp(regex.source, regex.flags.includes('g') ? regex.flags : `${regex.flags}g`);
    const found = new Set<string>();
    for (const match of html.matchAll(global)) {
      const value = match[1] ?? match[0];
      if (value) {
        found.add(value.replace(/\\\//g, '/'));
      }
    }
    return [...found];
  }

  private guessQuality(url: string): string | undefined {
    const match = url.match(/(\d{3,4})p/i);
    return match ? `${match[1]}p` : undefined;
  }
}
