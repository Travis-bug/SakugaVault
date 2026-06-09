"""Environment-driven configuration.

SECURITY / TRUST BOUNDARY:
    SCRAPERAPI_API_KEY is read here and used only inside this server-side
    container. It is never returned in any HTTP response and never reaches the
    browser or the C# API. This service has no host port mapping in
    docker-compose; it is reachable only on the internal Docker network by the
    SakugaVault API. Do not add CORS or a public route.

The target-site selectors are configuration on purpose: a high-throughput crawl
target is chosen per deployment, so its DOM shape lives in env, not in code.
"""

from __future__ import annotations

import os
import re
from dataclasses import dataclass, field

# Required env vars that were missing at load time. The service still boots so
# /health works and the C# fan-out can treat an unconfigured resolver as a
# failed candidate (clean 503), rather than crash-looping when left disabled.
_missing_required: list[str] = []


def _required(name: str) -> str:
    value = os.environ.get(name, "").strip()
    if not value:
        _missing_required.append(name)
    return value


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


def _compile(name: str, fallback: str) -> re.Pattern[str]:
    pattern = _optional(name, fallback)
    try:
        return re.compile(pattern, re.IGNORECASE)
    except re.error as error:
        raise ValueError(f"Environment variable {name} is not a valid regular expression: {error}") from error


@dataclass(frozen=True)
class ScraperApiConfig:
    api_key: str
    proxy_host: str
    render: bool
    # ScraperAPI's advanced anti-bot / Cloudflare-bypass tier.
    ultra_premium: bool
    country_code: str
    timeout_seconds: int

    def proxy_url(self) -> str:
        """Builds the ScraperAPI proxy-mode URL.

        Proxy mode keeps response.url as the real target (so relative links join
        correctly) while ScraperAPI renders JS and bypasses Cloudflare. Options
        are appended to the proxy username, dot-separated.
        """
        options = [f"render={'true' if self.render else 'false'}"]
        if self.country_code:
            options.append(f"country_code={self.country_code}")
        if self.ultra_premium:
            options.append("ultra_premium=true")
        username = ".".join(["scraperapi", *options])
        return f"http://{username}:{self.api_key}@{self.proxy_host}"


@dataclass(frozen=True)
class TargetConfig:
    provider_name: str
    base_url: str
    search_path: str
    search_item_css: str
    search_link_css: str
    search_title_css: str
    episode_item_css: str
    episode_link_css: str
    episode_number_attr: str
    episode_title_css: str
    episode_iframe_css: str
    stream_url_regex: re.Pattern[str]
    subtitle_url_regex: re.Pattern[str] | None


@dataclass(frozen=True)
class ServiceConfig:
    port: int
    host: str
    cache_ttl_seconds: int
    configured: bool
    missing: list[str] = field(default_factory=list)
    scraperapi: ScraperApiConfig = None  # type: ignore[assignment]
    target: TargetConfig = None  # type: ignore[assignment]


def load_config() -> ServiceConfig:
    _missing_required.clear()

    subtitle_pattern = _optional("TARGET_SUBTITLE_URL_REGEX", "")
    scraperapi = ScraperApiConfig(
        api_key=_required("SCRAPERAPI_API_KEY"),
        proxy_host=_optional("SCRAPERAPI_PROXY_HOST", "proxy-server.scraperapi.com:8001"),
        render=_bool("SCRAPERAPI_RENDER", True),
        ultra_premium=_bool("SCRAPERAPI_ULTRA_PREMIUM", True),
        country_code=_optional("SCRAPERAPI_COUNTRY_CODE", "us"),
        timeout_seconds=_int("SCRAPERAPI_TIMEOUT_SECONDS", 70),
    )
    target = TargetConfig(
        provider_name=_optional("TARGET_PROVIDER_NAME", "scrapy_scraperapi"),
        base_url=_required("TARGET_BASE_URL").rstrip("/"),
        search_path=_optional("TARGET_SEARCH_PATH", "/search?keyword={query}"),
        search_item_css=_optional("TARGET_SEARCH_ITEM_CSS", ".film_list-wrap .flw-item"),
        search_link_css=_optional("TARGET_SEARCH_LINK_CSS", "a.film-poster-ahref::attr(href)"),
        search_title_css=_optional("TARGET_SEARCH_TITLE_CSS", ".film-name a::text"),
        episode_item_css=_optional("TARGET_EPISODE_ITEM_CSS", ".ss-list a.ssl-item"),
        episode_link_css=_optional("TARGET_EPISODE_LINK_CSS", "::attr(href)"),
        episode_number_attr=_optional("TARGET_EPISODE_NUMBER_ATTR", "data-number"),
        episode_title_css=_optional("TARGET_EPISODE_TITLE_CSS", "::attr(title)"),
        episode_iframe_css=_optional("TARGET_EPISODE_IFRAME_CSS", "iframe::attr(src)"),
        stream_url_regex=_compile("TARGET_STREAM_URL_REGEX", r"(https?://[^\"']+\.m3u8[^\"']*)"),
        subtitle_url_regex=_compile("TARGET_SUBTITLE_URL_REGEX", subtitle_pattern) if subtitle_pattern else None,
    )

    missing = list(_missing_required)
    return ServiceConfig(
        port=_int("PORT", 3300),
        host=_optional("HOST", "0.0.0.0"),
        cache_ttl_seconds=_int("CACHE_TTL_SECONDS", 300),
        configured=len(missing) == 0,
        missing=missing,
        scraperapi=scraperapi,
        target=target,
    )


# Loaded once at import; the whole service shares this instance.
CONFIG = load_config()
