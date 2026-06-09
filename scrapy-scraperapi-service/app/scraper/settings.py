"""Scrapy settings for the on-demand resolver crawls.

These are built per CrawlerRunner rather than via a scrapy.cfg project, because
the spiders run inside a Flask service (see app/scraper/runner.py).
"""

from __future__ import annotations

import os

from app.config import CONFIG


def build_settings() -> dict[str, object]:
    return {
        "BOT_NAME": "sakugavault_scrapy",
        "ROBOTSTXT_OBEY": False,
        "LOG_LEVEL": os.environ.get("SCRAPY_LOG_LEVEL", "WARNING"),
        "TELNETCONSOLE_ENABLED": False,
        "COOKIES_ENABLED": False,
        # Let crochet own the reactor; don't force Scrapy's asyncio reactor.
        "TWISTED_REACTOR": None,
        # ScraperAPI proxy mode terminates TLS itself, so skip upstream verify.
        "DOWNLOADER_CLIENT_TLS_VERIFY": False,
        "DOWNLOAD_TIMEOUT": CONFIG.scraperapi.timeout_seconds,
        "CONCURRENT_REQUESTS": 8,
        "RETRY_ENABLED": True,
        "RETRY_TIMES": 2,
        # Route every request through ScraperAPI before the stock proxy mw (750).
        "DOWNLOADER_MIDDLEWARES": {
            "app.scraper.middlewares.ScraperApiProxyMiddleware": 100,
        },
        "USER_AGENT": (
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            "(KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36"
        ),
    }
