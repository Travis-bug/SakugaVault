/**
 * ScrapingBee fetch layer.
 *
 * We use Crawlee's gotScraping for the actual HTTP (browser-like TLS/headers,
 * HTTP2, retries) and route every target URL through the ScrapingBee render
 * API. ScrapingBee handles the JS rendering and the Cloudflare-grade proxy that
 * the old spec assigned to the "JS-heavy path"; render_js=true returns the page
 * after client-side scripts (and the video player config) have executed.
 *
 * The API key lives only in this process (see config.ts). The C# API calls this
 * service over the internal Docker network and never sees the key.
 */

import { gotScraping } from 'crawlee';
import type { ScrapingBeeConfig } from './config.ts';

export class ScrapingBeeClient {
  constructor(private readonly config: ScrapingBeeConfig) {}

  /**
   * Fetches a target URL through ScrapingBee and returns the rendered HTML.
   * `render` can be forced off for cheap, static pages (e.g. search) to save
   * ScrapingBee credits.
   */
  async fetchHtml(targetUrl: string, options: { render?: boolean } = {}): Promise<string> {
    const render = options.render ?? this.config.renderJs;
    const query = new URLSearchParams({
      api_key: this.config.apiKey,
      url: targetUrl,
      render_js: String(render),
      premium_proxy: String(this.config.premiumProxy),
      country_code: this.config.countryCode,
      block_resources: String(this.config.blockResources)
    });

    if (this.config.stealthProxy) {
      query.set('stealth_proxy', 'true');
    }
    if (render && this.config.waitMs > 0) {
      query.set('wait', String(this.config.waitMs));
    }

    const response = await gotScraping({
      url: `${this.config.endpoint}?${query.toString()}`,
      method: 'GET',
      timeout: { request: this.config.timeoutMs },
      retry: { limit: 2 },
      throwHttpErrors: false,
      responseType: 'text'
    });

    if (response.statusCode >= 400) {
      // ScrapingBee surfaces upstream/credit errors in the body; keep it short.
      const detail = String(response.body ?? '').slice(0, 200);
      throw new Error(`ScrapingBee request failed (${response.statusCode}) for ${targetUrl}: ${detail}`);
    }

    return String(response.body ?? '');
  }
}
