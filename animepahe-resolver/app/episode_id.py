"""Episode-id tagging, compatible with scraper-service's sv1 scheme.

The C# StreamScraperService takes the `id` we return from /info and passes it
straight back as ?episodeId= on /watch, so we encode the data the watch step
needs: for AnimePahe that is "<anime_session>/<episode_session>".

    sv1:<provider>:<base64url(payload)>

Search-result ids use an "sa:" prefix so /info can tell a real (resolvable) id
(the AnimePahe anime session) from a bare AniList numeric id; for the latter we
404 and let the C# layer fall through to the /meta/anilist/{title} search path.
base64url is unpadded to match the sv1 convention used by scraper-service.
"""

from __future__ import annotations

import base64

_SEARCH_ID_PREFIX = "sa:"
_DEFAULT_PROVIDER = "animepahe"


def _b64url_encode(value: str) -> str:
    return base64.urlsafe_b64encode(value.encode("utf-8")).decode("ascii").rstrip("=")


def _b64url_decode(value: str) -> str:
    padding = "=" * (-len(value) % 4)
    return base64.urlsafe_b64decode(value + padding).decode("utf-8")


def encode_search_id(anime_session: str) -> str:
    return _SEARCH_ID_PREFIX + _b64url_encode(anime_session)


def is_resolvable_search_id(value: str) -> bool:
    return value.startswith(_SEARCH_ID_PREFIX)


def decode_search_id(value: str) -> str:
    if not is_resolvable_search_id(value):
        raise ValueError("Not a resolvable AnimePahe search id.")
    return _b64url_decode(value[len(_SEARCH_ID_PREFIX):])


def encode_episode_id(anime_session: str, episode_session: str) -> str:
    return f"sv1:{_DEFAULT_PROVIDER}:{_b64url_encode(f'{anime_session}/{episode_session}')}"


def decode_episode_id(tagged_id: str) -> tuple[str, str]:
    """Returns (anime_session, episode_session)."""
    payload = tagged_id
    if tagged_id.startswith("sv1:"):
        parts = tagged_id.split(":")
        if len(parts) < 3:
            raise ValueError("Tagged episode id is malformed.")
        payload = _b64url_decode(":".join(parts[2:]))
    anime_session, _, episode_session = payload.partition("/")
    if not anime_session or not episode_session:
        raise ValueError("Episode id does not encode an anime/episode session pair.")
    return anime_session, episode_session
