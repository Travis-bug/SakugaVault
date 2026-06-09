"""Watch spider: episode page -> playable sources + subtitles.

Renders the episode page (and optionally one embedded iframe) through
ScraperAPI, then extracts stream/subtitle URLs via the configured regexes and
runs the foreign-language interceptor. Yields a single dict the Flask handler
merges with the request's language plan.
"""

from __future__ import annotations

import re

import scrapy

from app.config import CONFIG
from app.language import classify_foreign, normalize_language_code


def _unique_matches(pattern: re.Pattern[str], text: str) -> list[str]:
    found: list[str] = []
    seen: set[str] = set()
    for match in pattern.finditer(text):
        value = match.group(1) if match.groups() else match.group(0)
        if not value:
            continue
        value = value.replace("\\/", "/")
        if value not in seen:
            seen.add(value)
            found.append(value)
    return found


def _guess_quality(url: str) -> str | None:
    match = re.search(r"(\d{3,4})p", url, re.IGNORECASE)
    return f"{match.group(1)}p" if match else None


class WatchSpider(scrapy.Spider):
    name = "watch"
    episode_url: str = ""

    def start_requests(self):
        yield scrapy.Request(self.episode_url, callback=self.parse, dont_filter=True)

    def parse(self, response):
        target = CONFIG.target
        iframe_src = response.css(target.episode_iframe_css).get() if target.episode_iframe_css else None
        if iframe_src:
            iframe_url = response.urljoin(iframe_src)
            yield scrapy.Request(
                iframe_url,
                callback=self.extract,
                cb_kwargs={"referer": iframe_url},
                dont_filter=True,
            )
        else:
            yield from self.extract(response, referer=self.episode_url)

    def extract(self, response, referer: str):
        target = CONFIG.target
        text = response.text

        sources = [
            {
                "url": url,
                "quality": _guess_quality(url),
                "isM3U8": bool(re.search(r"\.m3u8(?:$|\?)", url, re.IGNORECASE)),
                "server": target.provider_name,
            }
            for url in _unique_matches(target.stream_url_regex, text)
        ]

        subtitles = []
        if target.subtitle_url_regex is not None:
            for url in _unique_matches(target.subtitle_url_regex, text):
                language = normalize_language_code(url)
                subtitles.append(
                    {
                        "url": url,
                        "language": language or "unknown",
                        "label": language.upper() if language else "Subtitle",
                        "kind": "subtitles",
                    }
                )

        foreign_warning = classify_foreign(text)
        if foreign_warning is None:
            for track in subtitles:
                foreign_warning = classify_foreign(track["label"])
                if foreign_warning:
                    break

        yield {
            "sources": sources,
            "subtitles": subtitles,
            "referer": referer,
            "foreign_warning": foreign_warning,
        }
