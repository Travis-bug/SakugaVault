"""Info spider: anime page -> episode list.

Yields {"id", "number", "title"} dicts, de-duplicated by episode number. The id
is the sv1:<provider>:<base64url(url)> tag that round-trips back to /watch.
"""

from __future__ import annotations

import scrapy

from app.config import CONFIG
from app.episode_id import encode_episode_id


class InfoSpider(scrapy.Spider):
    name = "info"
    anime_url: str = ""

    def start_requests(self):
        yield scrapy.Request(self.anime_url, callback=self.parse, dont_filter=True)

    def parse(self, response):
        target = CONFIG.target
        seen: set[float] = set()
        for index, item in enumerate(response.css(target.episode_item_css)):
            href = item.css(target.episode_link_css).get()
            if not href:
                continue

            number_raw = item.attrib.get(target.episode_number_attr) or str(index + 1)
            try:
                number = float(number_raw)
            except (TypeError, ValueError):
                number = float(index + 1)
            if number in seen:
                continue
            seen.add(number)

            title = (item.css(target.episode_title_css).get() or f"Episode {number:g}").strip()
            yield {
                "id": encode_episode_id(target.provider_name, response.urljoin(href)),
                "number": number,
                "title": title,
            }
