"""AnimePahe data layer.

Flow (all site-shell calls go through FlareSolverr to clear Cloudflare):
    search   GET /api?m=search&q=...            -> anime sessions
    episodes GET /api?m=release&id=<s>&page=N   -> episode sessions (+ audio)
    play     GET /play/<anime>/<episode>        -> kwik embed buttons
    stream   GET <kwik>/e/<id>  (direct + Referer) -> packed JS -> m3u8

The kwik embed is fetched directly (not via FlareSolverr) because it only needs
a Referer header, which FlareSolverr cannot set; from a residential egress IP
kwik answers 200 with the packed player script. The stream URL it yields plays
with `Referer: https://kwik.cx/`, which the watch payload hands back to the C#
secure proxy.
"""

from __future__ import annotations

import re
from urllib.parse import urljoin

import requests

from app.config import CONFIG
from app.flaresolverr import FLARESOLVERR

_KWIK_BUTTON = re.compile(
    r'<button[^>]*data-src="(?P<url>https://kwik\.\w+/e/\w+)"[^>]*class="dropdown-item',
    re.IGNORECASE,
)
_PACKER = re.compile(
    r"\}\('(?P<p>(?:[^'\\]|\\.)*)',(?P<a>\d+),(?P<c>\d+),'(?P<k>(?:[^'\\]|\\.)*)'\.split\('\|'\)",
    re.DOTALL,
)
_M3U8 = re.compile(r"https?://[^\s'\"\\]+\.m3u8[^\s'\"\\]*")


def _base() -> str:
    return CONFIG.animepahe_bases[0]


# ---- discovery -------------------------------------------------------------

def search(query: str) -> list[dict]:
    payload = FLARESOLVERR.get_json(f"{_base()}/api?m=search&q={requests.utils.quote(query)}")
    results = []
    for item in payload.get("data") or []:
        session = item.get("session")
        title = item.get("title")
        if session and title:
            results.append({"session": session, "title": title})
    return results


def episodes(anime_session: str) -> list[dict]:
    collected: dict[int, dict] = {}
    page = 1
    last_page = 1
    while page <= last_page:
        payload = FLARESOLVERR.get_json(
            f"{_base()}/api?m=release&id={anime_session}&sort=episode_asc&page={page}"
        )
        last_page = int(payload.get("last_page") or 1)
        for item in payload.get("data") or []:
            number = item.get("episode")
            session = item.get("session")
            if number is None or not session:
                continue
            number = int(round(float(number)))
            # First session per number wins; AnimePahe lists episodes once.
            collected.setdefault(
                number,
                {
                    "number": number,
                    "session": session,
                    "title": item.get("title") or f"Episode {number}",
                    "audio": item.get("audio"),
                },
            )
        page += 1
    return [collected[number] for number in sorted(collected)]


# ---- stream extraction -----------------------------------------------------

def _packer_unpack(packed: str, radix: str, count: str, keywords: str) -> str:
    radix_n = int(radix)
    count_n = int(count)
    words = keywords.split("|")
    digits = "0123456789abcdefghijklmnopqrstuvwxyz"

    def encode(n: int) -> str:
        head = "" if n < radix_n else encode(n // radix_n)
        rem = n % radix_n
        return head + (chr(rem + 29) if rem > 35 else digits[rem])

    table = {encode(i): (words[i] or encode(i)) for i in range(count_n)}
    source = packed.encode("utf-8").decode("unicode_escape")
    return re.sub(r"\b\w+\b", lambda m: table.get(m.group(0), m.group(0)), source)


def _extract_m3u8_from_kwik_page(page_html: str) -> str | None:
    # kwik ships a decoy packed block plus the real one; scan all, take the m3u8.
    for match in _PACKER.finditer(page_html):
        unpacked = _packer_unpack(match.group("p"), match.group("a"), match.group("c"), match.group("k"))
        found = _M3U8.search(unpacked)
        if found:
            return found.group(0)
    return None


def _fetch_kwik(kwik_url: str) -> str | None:
    try:
        response = requests.get(
            kwik_url,
            headers={
                "User-Agent": CONFIG.request_user_agent,
                "Referer": CONFIG.kwik_fetch_referer,
                "Cookie": "__ddg2_=;",
            },
            timeout=20,
        )
    except requests.RequestException:
        return None
    if response.status_code != 200:
        return None
    return _extract_m3u8_from_kwik_page(response.text)


def _parse_play_buttons(play_html: str) -> list[dict]:
    buttons = []
    for match in _KWIK_BUTTON.finditer(play_html):
        tag = match.group(0)
        resolution = re.search(r'data-resolution="(\d+)"', tag)
        audio = re.search(r'data-audio="(\w+)"', tag)
        buttons.append(
            {
                "url": match.group("url"),
                "resolution": int(resolution.group(1)) if resolution else 0,
                "audio": (audio.group(1) if audio else "jpn").lower(),
            }
        )
    return buttons


def resolve_stream(anime_session: str, episode_session: str, audio_preference: str) -> dict | None:
    """audio_preference is the plan's audio language ('ja' or 'en').

    'jpn' buttons are Japanese audio with burned-in English subtitles; 'eng'
    buttons are the English dub. We try the preferred audio first, then fall back
    to the other (a Japanese/English-hardsub stream is still English for the
    viewer), picking the highest resolution that actually yields a stream.
    """
    play_html = FLARESOLVERR.get(f"{_base()}/play/{anime_session}/{episode_session}")
    buttons = _parse_play_buttons(play_html)
    if not buttons:
        return None

    wanted = "eng" if audio_preference == "en" else "jpn"
    ordered = sorted(
        buttons,
        key=lambda b: (b["audio"] != wanted, -b["resolution"]),
    )
    for button in ordered:
        m3u8 = _fetch_kwik(button["url"])
        if m3u8:
            _prewarm_cdn(m3u8)
            return {
                "url": m3u8,
                "quality": f"{button['resolution']}p" if button["resolution"] else None,
                "audio": "en" if button["audio"] == "eng" else "ja",
                "referer": CONFIG.stream_referer,
            }
    return None


def _warm(url: str, headers: dict) -> None:
    try:
        requests.get(url, headers=headers, timeout=15)
    except requests.RequestException:
        pass


def _prewarm_cdn(m3u8_url: str) -> None:
    """Fetch the playlist, its AES key(s) and first segments with `requests` so
    the kwik CDN caches them.

    The C# stream proxy fetches with .NET HttpClient, whose TLS fingerprint the
    kwik key endpoint rejects (403) on a cache miss, while `requests` (like a
    browser/curl) is accepted. Warming the key here means the proxy's later
    fetch is a cache HIT and succeeds, so decryption — and playback — works on
    first watch instead of only after the key is incidentally cached.
    """
    headers = {
        "User-Agent": CONFIG.request_user_agent,
        "Referer": CONFIG.stream_referer,
        "Accept": "*/*",
    }
    try:
        response = requests.get(m3u8_url, headers=headers, timeout=20)
    except requests.RequestException:
        return
    if response.status_code != 200:
        return
    body = response.text

    def warm_playlist(playlist_url: str, text: str) -> None:
        for uri in re.findall(r'URI="([^"]+)"', text):  # EXT-X-KEY / EXT-X-MAP
            _warm(urljoin(playlist_url, uri), headers)
        media = [ln.strip() for ln in text.splitlines() if ln.strip() and not ln.startswith("#")]
        for line in media[:2]:  # first couple of segments for instant start
            _warm(urljoin(playlist_url, line), headers)

    # A master playlist points at variant playlists; resolve one level so the
    # key (which lives on the media playlist) gets warmed too.
    if "#EXT-X-STREAM-INF" in body:
        variants = [ln.strip() for ln in body.splitlines() if ln.strip() and not ln.startswith("#")]
        if variants:
            variant_url = urljoin(m3u8_url, variants[0])
            try:
                variant = requests.get(variant_url, headers=headers, timeout=15)
                if variant.status_code == 200:
                    warm_playlist(variant_url, variant.text)
            except requests.RequestException:
                pass
    else:
        warm_playlist(m3u8_url, body)
