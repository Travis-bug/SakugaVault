"""AnimePahe + FlareSolverr resolver (HTTP surface).

Exposes the same Consumet-compatible contract StreamScraperService fans out to,
so it plugs in as another entry under Scrapers__PlaybackResolvers with no C#
changes. The browser never reaches this service; only the C# API does, over the
internal Docker network.

    GET /health
    GET /meta/anilist/{query}        -> search
    GET /meta/anilist/info/{id}      -> episodes (404 for bare AniList ids)
    GET /anime/{provider}/watch      -> playable English-hardsub source
"""

from __future__ import annotations

import threading
import time
from typing import Any, Callable

from flask import Flask, jsonify, request

from app import animepahe
from app.config import CONFIG
from app.episode_id import (
    decode_episode_id,
    decode_search_id,
    encode_episode_id,
    encode_search_id,
    is_resolvable_search_id,
)
from app.flaresolverr import FLARESOLVERR, FlareSolverrError
from app.language import build_language_plan

app = Flask(__name__)

# Small TTL cache so the C# stampede lock isn't the only thing shielding the
# (slow, browser-driven) FlareSolverr path from repeated identical lookups.
_cache: dict[str, tuple[float, Any]] = {}
_cache_lock = threading.Lock()


def _cached(key: str, factory: Callable[[], Any]) -> Any:
    now = time.monotonic()
    with _cache_lock:
        hit = _cache.get(key)
        if hit and hit[0] > now:
            return hit[1]
    value = factory()
    with _cache_lock:
        _cache[key] = (now + CONFIG.cache_ttl_seconds, value)
    return value


@app.get("/")
@app.get("/health")
def health():
    return jsonify(
        {
            "service": "sakugavault-animepahe-flaresolverr",
            "status": "online",
            "provider": CONFIG.provider_name,
            "flaresolverr": CONFIG.flaresolverr_url,
        }
    )


@app.get("/meta/anilist/<query>")
def search(query: str):
    query = query.strip()
    if not query:
        return jsonify({"error": "missing_query", "message": "Search query is required."}), 400

    def run():
        return [
            {"id": encode_search_id(item["session"]), "title": item["title"]}
            for item in animepahe.search(query)
        ]

    try:
        results = _cached(f"search:{query.lower()}", run)
    except FlareSolverrError as error:
        return jsonify({"error": "resolver_unavailable", "message": str(error)}), 503
    return jsonify({"results": results})


@app.get("/meta/anilist/info/<path:raw_id>")
def info(raw_id: str):
    raw_id = raw_id.strip()
    if not raw_id:
        return jsonify({"error": "missing_id", "message": "An id is required."}), 400

    # Bare AniList ids aren't resolvable here; 404 so the C# layer falls through
    # to /meta/anilist/{title}, the same contract scraper-service uses.
    if not is_resolvable_search_id(raw_id):
        return (
            jsonify(
                {
                    "error": "unresolvable_id",
                    "message": "This resolver maps titles, not raw AniList ids. Use the search route first.",
                }
            ),
            404,
        )

    anime_session = decode_search_id(raw_id)

    def run():
        return [
            {
                "id": encode_episode_id(anime_session, episode["session"]),
                "number": episode["number"],
                "title": episode["title"],
            }
            for episode in animepahe.episodes(anime_session)
        ]

    try:
        episodes = _cached(f"info:{anime_session}", run)
    except FlareSolverrError as error:
        return jsonify({"error": "resolver_unavailable", "message": str(error)}), 503
    if not episodes:
        return jsonify({"error": "no_episodes", "message": "No episodes were found for this title."}), 404

    return jsonify({"sourceProvider": CONFIG.provider_name, "episodes": episodes})


@app.get("/meta/anilist/watch")
@app.get("/anime/<provider>/watch")
def watch(provider: str | None = None):
    episode_id = request.args.get("episodeId")
    if not episode_id:
        return jsonify({"error": "missing_episode_id", "message": "episodeId query parameter is required."}), 400

    plan = build_language_plan(
        request.args.get("preferredLanguage") or request.args.get("language"),
        request.args.get("audioLanguage"),
        request.args.get("subtitleLanguage"),
    )

    try:
        anime_session, episode_session = decode_episode_id(episode_id)
    except ValueError as error:
        return jsonify({"error": "bad_episode_id", "message": str(error)}), 400

    def run():
        return animepahe.resolve_stream(anime_session, episode_session, plan.audio_language)

    try:
        stream = _cached(f"watch:{episode_session}:{plan.audio_language}", run)
    except FlareSolverrError as error:
        return jsonify({"error": "resolver_unavailable", "message": str(error)}), 503

    if not stream:
        return (
            jsonify(
                {
                    "error": "no_sources",
                    "message": "AnimePahe did not return a playable stream for this episode.",
                }
            ),
            404,
        )

    # AnimePahe 'jpn' streams carry burned-in English subtitles (no separate
    # track); 'eng' streams are the English dub. Either way the content is
    # English, so this is a verified 'provider' source, not a hardcoded one.
    audio_language = stream["audio"]
    subtitle_language = "en" if audio_language == "ja" else "off"
    payload = {
        "headers": {"Referer": stream["referer"]},
        "sources": [
            {
                "url": stream["url"],
                "quality": stream["quality"],
                "isM3U8": True,
                "server": CONFIG.provider_name,
            }
        ],
        "audioLanguage": audio_language,
        "subtitleLanguage": subtitle_language,
        "languageSource": "provider",
        "subtitles": [],
    }
    return jsonify(payload)


def _warm_in_background() -> None:
    try:
        FLARESOLVERR.warm()
        print("AnimePahe resolver: FlareSolverr session warmed.", flush=True)
    except FlareSolverrError as error:
        print(f"AnimePahe resolver: warm-up deferred ({error}).", flush=True)


def main() -> None:
    from waitress import serve

    if CONFIG.warm_on_start:
        threading.Thread(target=_warm_in_background, daemon=True).start()
    print(
        f"AnimePahe/FlareSolverr resolver listening on http://{CONFIG.host}:{CONFIG.port} "
        f"(provider={CONFIG.provider_name}, flaresolverr={CONFIG.flaresolverr_url})",
        flush=True,
    )
    serve(app, host=CONFIG.host, port=CONFIG.port)


if __name__ == "__main__":
    main()
