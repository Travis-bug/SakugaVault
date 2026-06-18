"""Environment-driven configuration for the AnimePahe + FlareSolverr resolver.

SECURITY / TRUST BOUNDARY:
    This service is internal-only. In docker-compose it uses `expose`, not
    `ports`, so only the C# API reaches it on the internal Docker network. It
    talks to FlareSolverr (also internal) to clear Cloudflare on animepahe.com.
    No secret/API key is involved; nothing here is returned to the browser.

Why AnimePahe: it serves English-hardsubbed streams (ideal for an English
audience) and is reachable from a residential egress IP. Its only gate is
Cloudflare on the site shell, which FlareSolverr (headless Chrome) clears; the
kwik embed that hosts the stream is fetched directly with a Referer header.
"""

from __future__ import annotations

import os
from dataclasses import dataclass


def _optional(name: str, fallback: str) -> str:
    value = os.environ.get(name, "").strip()
    return value or fallback


def _int(name: str, fallback: int) -> int:
    raw = os.environ.get(name, "").strip()
    try:
        parsed = int(raw)
        return parsed if parsed > 0 else fallback
    except ValueError:
        return fallback


def _bool(name: str, fallback: bool) -> bool:
    raw = os.environ.get(name)
    if raw is None:
        return fallback
    return raw.strip().lower() in {"1", "true", "yes", "on"}


@dataclass(frozen=True)
class ServiceConfig:
    port: int
    host: str
    provider_name: str
    # FlareSolverr clears Cloudflare on the AnimePahe site shell.
    flaresolverr_url: str
    flaresolverr_timeout_ms: int
    # Primary + fallback AnimePahe domains (the project's domain rotates).
    animepahe_bases: tuple[str, ...]
    # Referer for the direct (non-FlareSolverr) fetch of the kwik embed page.
    kwik_fetch_referer: str
    # Referer the extracted m3u8 needs at playback time (handed to the C# proxy).
    stream_referer: str
    request_user_agent: str
    cache_ttl_seconds: int
    warm_on_start: bool
    # Background keep-alive interval so Cloudflare clearance never goes stale on
    # the user path.
    heartbeat_interval_seconds: int


def load_config() -> ServiceConfig:
    bases = _optional("ANIMEPAHE_BASES", "https://animepahe.com,https://animepahe.org")
    return ServiceConfig(
        port=_int("PORT", 3400),
        host=_optional("HOST", "0.0.0.0"),
        provider_name=_optional("PROVIDER_NAME", "animepahe"),
        flaresolverr_url=_optional("FLARESOLVERR_URL", "http://flaresolverr:8191").rstrip("/"),
        flaresolverr_timeout_ms=_int("FLARESOLVERR_TIMEOUT_MS", 90000),
        animepahe_bases=tuple(b.strip().rstrip("/") for b in bases.split(",") if b.strip()),
        kwik_fetch_referer=_optional("KWIK_FETCH_REFERER", "https://animepahe.com/"),
        stream_referer=_optional("STREAM_REFERER", "https://kwik.cx/"),
        request_user_agent=_optional(
            "REQUEST_USER_AGENT",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        ),
        cache_ttl_seconds=_int("CACHE_TTL_SECONDS", 600),
        warm_on_start=_bool("WARM_ON_START", True),
        heartbeat_interval_seconds=_int("HEARTBEAT_INTERVAL_SECONDS", 180),
    )


CONFIG = load_config()
