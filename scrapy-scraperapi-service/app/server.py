"""SakugaVault Scrapy + ScraperAPI resolver (HTTP surface).

Exposes the same Consumet-compatible contract that StreamScraperService fans out
to, so it plugs in as another entry under Scrapers__PlaybackResolvers with no C#
changes. The browser never reaches this service; only the C# API does, over the
internal Docker network.

    GET /health
    GET /meta/anilist/{query}        -> search
    GET /meta/anilist/info/{id}      -> episodes (404 for bare AniList ids)
    GET /anime/{provider}/watch      -> playable sources + subtitles
"""

from __future__ import annotations

import time
from typing import Any, Callable

from flask import Flask, jsonify, request

from app.config import CONFIG
from app.episode_id import decode_episode_id, decode_search_id, is_resolvable_search_id
from app.language import build_language_plan

app = Flask(__name__)

# Small TTL cache so the C# stampede lock isn't the only thing shielding the
# (paid) ScraperAPI budget from repeated identical lookups.
_cache: dict[str, tuple[float, Any]] = {}


def _cached(key: str, factory: Callable[[], Any]) -> Any:
    hit = _cache.get(key)
    now = time.monotonic()
    if hit and hit[0] > now:
        return hit[1]
    value = factory()
    _cache[key] = (now + CONFIG.cache_ttl_seconds, value)
    return value


@app.get("/")
@app.get("/health")
def health():
    return jsonify(
        {
            "service": "sakugavault-scrapy-scraperapi",
            "status": "online" if CONFIG.configured else "unconfigured",
            "provider": CONFIG.target.provider_name,
            "missing": CONFIG.missing,
        }
    )


@app.before_request
def _gate_unconfigured():
    # /health stays open; resolver routes return 503 until fully configured, so
    # the C# fan-out records a clean failed candidate instead of a hang.
    if request.path in ("/", "/health"):
        return None
    if not CONFIG.configured:
        return (
            jsonify(
                {
                    "error": "resolver_unconfigured",
                    "message": f"Scrapy/ScraperAPI resolver is not configured. Missing: {', '.join(CONFIG.missing)}.",
                }
            ),
            503,
        )
    return None


@app.get("/meta/anilist/<query>")
def search(query: str):
    query = query.strip()
    if not query:
        return jsonify({"error": "missing_query", "message": "Search query is required."}), 400

    from app.scraper.runner import run_search

    results = _cached(f"search:{query}", lambda: run_search(query))
    return jsonify({"results": results})


@app.get("/meta/anilist/info/<path:raw_id>")
def info(raw_id: str):
    raw_id = raw_id.strip()
    if not raw_id:
        return jsonify({"error": "missing_id", "message": "An id is required."}), 400

    # Bare AniList ids aren't resolvable by a title-driven site crawler. Return
    # 404 so StreamScraperService falls through to /meta/anilist/{title}.
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

    anime_url = decode_search_id(raw_id)

    from app.scraper.runner import run_info

    episodes = _cached(f"info:{anime_url}", lambda: run_info(anime_url))
    if not episodes:
        return jsonify({"error": "no_episodes", "message": "No episodes were found for this title."}), 404

    return jsonify({"sourceProvider": CONFIG.target.provider_name, "episodes": episodes})


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

    _, episode_url = decode_episode_id(episode_id)

    from app.scraper.runner import run_watch

    cache_key = f"watch:{episode_url}:{plan.audio_language}:{plan.subtitle_language}"
    items = _cached(cache_key, lambda: run_watch(episode_url))
    item = items[0] if items else None
    sources = item["sources"] if item else []
    if not sources:
        return (
            jsonify(
                {
                    "error": "no_sources",
                    "message": "The resolver could not extract a playable stream from this episode.",
                }
            ),
            404,
        )

    foreign_warning = item["foreign_warning"]
    is_hardcoded = foreign_warning is not None
    payload: dict[str, Any] = {
        "headers": {"Referer": item["referer"]},
        "sources": sources,
        "audioLanguage": None if is_hardcoded else plan.audio_language,
        "subtitleLanguage": None if is_hardcoded else plan.subtitle_language,
        "languageSource": "hardcoded" if is_hardcoded else "provider",
        "subtitles": item["subtitles"],
    }
    if foreign_warning:
        payload["languageWarning"] = foreign_warning
    return jsonify(payload)


def main() -> None:
    from waitress import serve

    print(
        f"Scrapy/ScraperAPI resolver listening on http://{CONFIG.host}:{CONFIG.port} "
        f"(configured={CONFIG.configured}, provider={CONFIG.target.provider_name})",
        flush=True,
    )
    serve(app, host=CONFIG.host, port=CONFIG.port)


if __name__ == "__main__":
    main()
