"""Search spider: title query -> candidate titles.

Yields {"id", "title"} dicts. The id is an "sa:"-prefixed, resolvable handle so
the info spider can reopen the anime page later (see app/episode_id.py).
"""

from __future__ import annotations

from urllib.parse import quote

import scrapy

from app.config import CONFIG
from app.episode_id import encode_search_id


class SearchSpider(scrapy.Spider):
    name = "search"
    # Set via CrawlerRunner kwargs (Scrapy copies kwargs onto the instance).
    query: str = ""

    def start_requests(self):
        target = CONFIG.target
        url = target.base_url + target.search_path.replace("{query}", quote(self.query or ""))
        yield scrapy.Request(url, callback=self.parse, dont_filter=True)

    def parse(self, response):
        target = CONFIG.target
        for item in response.css(target.search_item_css):
            href = item.css(target.search_link_css).get()
            if not href:
                continue
            title = (item.css(target.search_title_css).get() or "").strip()
            if not title:
                continue
            yield {"id": encode_search_id(response.urljoin(href)), "title": title}
