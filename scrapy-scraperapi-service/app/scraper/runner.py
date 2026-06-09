"""Runs Scrapy spiders on demand from the Flask service.

Scrapy is built on Twisted's reactor, which can start only once per process and
cannot be driven directly from a request handler. Crochet runs that reactor in a
background thread and lets a synchronous caller block for a spider's result, so
each HTTP request can trigger a crawl and get its items back.

Importing this module calls crochet.setup() (starts the reactor), so the Flask
server imports it lazily — only when a configured request actually needs a crawl.
"""

from __future__ import annotations

import crochet

crochet.setup()

from scrapy import signals  # noqa: E402  (must follow crochet.setup)
from scrapy.crawler import CrawlerRunner  # noqa: E402

from app.config import CONFIG  # noqa: E402
from app.scraper.settings import build_settings  # noqa: E402
from app.scraper.spiders.info import InfoSpider  # noqa: E402
from app.scraper.spiders.search import SearchSpider  # noqa: E402
from app.scraper.spiders.watch import WatchSpider  # noqa: E402

# Generous ceiling: ScraperAPI render + Cloudflare bypass can be slow.
_RUN_TIMEOUT = CONFIG.scraperapi.timeout_seconds + 30


@crochet.wait_for(timeout=_RUN_TIMEOUT)
def _run(spider_cls, **kwargs):
    """Runs one spider to completion and returns the list of scraped items."""
    runner = CrawlerRunner(build_settings())
    items: list[dict] = []

    crawler = runner.create_crawler(spider_cls)
    crawler.signals.connect(
        lambda item, **_: items.append(dict(item)),
        signal=signals.item_scraped,
    )

    deferred = runner.crawl(crawler, **kwargs)
    deferred.addCallback(lambda _: items)
    return deferred


def run_search(query: str) -> list[dict]:
    return _run(SearchSpider, query=query)


def run_info(anime_url: str) -> list[dict]:
    return _run(InfoSpider, anime_url=anime_url)


def run_watch(episode_url: str) -> list[dict]:
    return _run(WatchSpider, episode_url=episode_url)
