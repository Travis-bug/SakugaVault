"""Downloader middleware that routes every request through ScraperAPI.

This is the "route requests through ScraperAPI with Cloudflare bypass enabled"
piece of the pipeline. It uses ScraperAPI proxy mode: the spider requests the
real target URL (so response.url and urljoin stay correct), and this middleware
attaches the ScraperAPI proxy — which renders JS and bypasses Cloudflare —
before Scrapy's stock HttpProxyMiddleware (priority 750) consumes meta['proxy'].

The API key lives only in this process (see app/config.py). It is never logged
or returned to a caller.
"""

from __future__ import annotations

from app.config import CONFIG


class ScraperApiProxyMiddleware:
    def __init__(self, proxy_url: str) -> None:
        self._proxy_url = proxy_url

    @classmethod
    def from_crawler(cls, crawler):  # noqa: ANN001 - Scrapy hook signature
        return cls(CONFIG.scraperapi.proxy_url())

    def process_request(self, request, spider):  # noqa: ANN001 - Scrapy hook signature
        # Set only if a request hasn't opted out (e.g. a future direct call).
        request.meta.setdefault("proxy", self._proxy_url)
        return None
