"""Episode-id tagging, compatible with scraper-service's sv1 scheme.

The C# StreamScraperService takes the `id` we return from /info and passes it
straight back as ?episodeId= on /watch, so we encode the absolute episode URL.

    sv1:<provider>:<base64url(url)>

Search-result ids use an "sa:" prefix so /info can tell a real (resolvable) id
from a bare AniList numeric id; for the latter we 404 and let the C# layer fall
through to the /meta/anilist/{title} search path. base64url is unpadded to match
the sv1 convention used by scraper-service.
"""

from __future__ import annotations

import base64

_SEARCH_ID_PREFIX = "sa:"


def _b64url_encode(value: str) -> str:
    return base64.urlsafe_b64encode(value.encode("utf-8")).decode("ascii").rstrip("=")


def _b64url_decode(value: str) -> str:
    padding = "=" * (-len(value) % 4)
    return base64.urlsafe_b64decode(value + padding).decode("utf-8")


def encode_search_id(anime_page_url: str) -> str:
    return _SEARCH_ID_PREFIX + _b64url_encode(anime_page_url)


def is_resolvable_search_id(value: str) -> bool:
    return value.startswith(_SEARCH_ID_PREFIX)


def decode_search_id(value: str) -> str:
    if not is_resolvable_search_id(value):
        raise ValueError("Not a resolvable scrapy search id.")
    return _b64url_decode(value[len(_SEARCH_ID_PREFIX):])


def encode_episode_id(provider: str, episode_page_url: str) -> str:
    return f"sv1:{provider}:{_b64url_encode(episode_page_url)}"


def decode_episode_id(tagged_id: str) -> tuple[str, str]:
    if not tagged_id.startswith("sv1:"):
        # Untagged ids are assumed to already be a direct episode URL.
        return "scrapy_scraperapi", tagged_id
    parts = tagged_id.split(":")
    if len(parts) < 3:
        raise ValueError("Tagged episode id is malformed.")
    return parts[1] or "scrapy_scraperapi", _b64url_decode(":".join(parts[2:]))
