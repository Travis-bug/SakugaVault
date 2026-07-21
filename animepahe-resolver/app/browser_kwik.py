"""Headless-Chromium kwik fetcher (the free-playback egress).

kwik gates its embed page behind a Cloudflare JS challenge and only hands over
the stream URL when the request carries `Referer: animepahe.com`. Getting the
stream therefore needs THREE things at once:

  1. a real browser        -> solves the Cloudflare JS challenge (a plain HTTP
                              client, even one that fakes Chrome's TLS, is 403'd);
  2. a residential IP      -> kwik bans datacenter ranges (our host is one);
  3. Referer: animepahe    -> without it kwik serves the player shell but omits
                              the source, so no m3u8 ever appears.

FlareSolverr covers (1)+(2) but cannot set the Referer; a raw proxy request
covers (2)+(3) but not (1). Only a browser we drive ourselves covers all three,
so we run Chromium via Playwright, routed through the residential proxy, with
the Referer set, and capture the m3u8 straight off the network as the player
requests it.

Bandwidth discipline (the proxy is metered / prepaid): every image, font,
stylesheet, media segment and the m3u8 body itself is aborted at the router —
we keep only the URL of the m3u8 request, never its bytes — so each fetch costs
a few hundred KB, not a whole video.

Threading: Playwright's sync API is single-threaded, so a dedicated worker
thread owns the browser and processes fetches off a queue. That also naturally
serializes kwik fetches (fine: resolves are already slow and low-volume).
"""

from __future__ import annotations

import base64
import queue
import socket
import threading
from urllib.parse import urlsplit

from app.config import CONFIG

_M3U8_HINT = ".m3u8"
# Resource types that only cost bandwidth; the m3u8 URL is captured from the
# player's XHR/fetch, which is not one of these.
_BLOCKED_TYPES = {"image", "media", "font", "stylesheet"}


# --- local CONNECT-forwarding proxy -----------------------------------------
# BestProxy expects preemptive Proxy-Authorization on the HTTPS CONNECT (as
# requests/curl send) but never issues a 407 challenge. Chromium waits for that
# 407, so its HTTPS tunnel dies with ERR_TUNNEL_CONNECTION_FAILED even though
# HTTP works. This tiny local forwarder bridges the gap: Chromium connects to it
# with NO auth, and it opens the upstream CONNECT with the Basic auth header
# baked in, then pipes bytes both ways. Rotation still lives in the upstream
# username, so every upstream connection still gets a fresh residential IP.


def _relay(src: socket.socket, dst: socket.socket) -> None:
    try:
        while True:
            data = src.recv(65536)
            if not data:
                break
            dst.sendall(data)
    except OSError:
        pass
    finally:
        for s in (src, dst):
            try:
                s.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass


class _UpstreamAuthProxy:
    """127.0.0.1 forwarder that injects upstream Proxy-Authorization on CONNECT."""

    def __init__(self) -> None:
        self._port: int | None = None
        self._lock = threading.Lock()

    def ensure_running(self) -> int | None:
        if not CONFIG.kwik_proxy_url:
            return None
        with self._lock:
            if self._port is not None:
                return self._port
            parts = urlsplit(CONFIG.kwik_proxy_url)
            up_host, up_port = parts.hostname, parts.port or 80
            auth = base64.b64encode(
                f"{parts.username}:{parts.password}".encode()
            ).decode()

            listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            listener.bind(("127.0.0.1", 0))
            listener.listen(64)
            self._port = listener.getsockname()[1]

            threading.Thread(
                target=self._serve,
                args=(listener, up_host, up_port, auth),
                daemon=True,
                name="kwik-proxy-forwarder",
            ).start()
            return self._port

    def _serve(self, listener, up_host, up_port, auth) -> None:
        while True:
            try:
                client, _ = listener.accept()
            except OSError:
                break
            threading.Thread(
                target=self._handle,
                args=(client, up_host, up_port, auth),
                daemon=True,
            ).start()

    def _handle(self, client: socket.socket, up_host, up_port, auth) -> None:
        try:
            request = b""
            while b"\r\n\r\n" not in request:
                chunk = client.recv(4096)
                if not chunk:
                    client.close()
                    return
                request += chunk
            # Chromium sends: CONNECT host:port HTTP/1.1 ...
            line = request.split(b"\r\n", 1)[0].decode("latin-1")
            method, target, _ = line.split(" ", 2)
            if method.upper() != "CONNECT":
                client.close()
                return

            upstream = socket.create_connection((up_host, up_port), timeout=20)
            upstream.sendall(
                f"CONNECT {target} HTTP/1.1\r\nHost: {target}\r\n"
                f"Proxy-Authorization: Basic {auth}\r\n"
                f"Proxy-Connection: Keep-Alive\r\n\r\n".encode()
            )
            resp = b""
            while b"\r\n\r\n" not in resp:
                chunk = upstream.recv(4096)
                if not chunk:
                    break
                resp += chunk
            status = resp.split(b"\r\n", 1)[0] if resp else b""
            if b" 200 " not in status:
                client.sendall(b"HTTP/1.1 502 Bad Gateway\r\n\r\n")
                client.close()
                upstream.close()
                return
            # Tell Chromium the tunnel is up, then pipe raw bytes both ways.
            client.sendall(b"HTTP/1.1 200 Connection established\r\n\r\n")
            threading.Thread(target=_relay, args=(client, upstream), daemon=True).start()
            _relay(upstream, client)
        except Exception:
            try:
                client.close()
            except OSError:
                pass


_UPSTREAM_PROXY = _UpstreamAuthProxy()


def _proxy_settings() -> dict | None:
    """Playwright proxy dict pointing at the local auth-injecting forwarder.
    Returns None when no proxy is configured."""
    port = _UPSTREAM_PROXY.ensure_running()
    if port is None:
        return None
    return {"server": f"http://127.0.0.1:{port}"}


class _BrowserKwikFetcher:
    def __init__(self) -> None:
        self._queue: "queue.Queue" = queue.Queue()
        self._started = False
        self._start_lock = threading.Lock()

    def _ensure_worker(self) -> None:
        with self._start_lock:
            if self._started:
                return
            threading.Thread(target=self._run, daemon=True, name="kwik-browser").start()
            self._started = True

    def _run(self) -> None:
        # Imported lazily so the module loads even where Playwright isn't present
        # (e.g. unit hosts); the worker only starts when a fetch is requested.
        from playwright.sync_api import sync_playwright

        proxy = _proxy_settings()
        try:
            with sync_playwright() as playwright:
                browser = playwright.chromium.launch(
                    args=["--no-sandbox", "--disable-dev-shm-usage"],
                )
                print("[kwik] browser worker ready", flush=True)
                while True:
                    kwik_url, holder, done = self._queue.get()
                    try:
                        holder["m3u8"] = self._fetch(browser, proxy, kwik_url)
                    except Exception as error:  # never let the worker die
                        holder["m3u8"] = None
                        print(f"[kwik] browser error: {str(error)[:120]}", flush=True)
                    finally:
                        done.set()
        except Exception as fatal:
            print(f"[kwik] browser worker terminated: {str(fatal)[:120]}", flush=True)
            # Drain any queued waiters so callers don't hang on a dead worker.
            self._started = False
            while not self._queue.empty():
                try:
                    _, holder, done = self._queue.get_nowait()
                    holder["m3u8"] = None
                    done.set()
                except queue.Empty:
                    break

    def _fetch(self, browser, proxy: dict | None, kwik_url: str) -> str | None:
        # Some residential IPs are WAF-blocked (403 "Attention Required") rather
        # than challenged; a fresh context rotates the proxy IP, so retry.
        for attempt in (1, 2, 3):
            context = browser.new_context(
                proxy=proxy,
                ignore_https_errors=True,  # the proxy re-signs TLS (its own CA)
                user_agent=CONFIG.request_user_agent,
                extra_http_headers={"Referer": CONFIG.kwik_fetch_referer},
            )
            captured: dict[str, str | None] = {"url": None}

            def route_handler(route):
                request = route.request
                url = request.url
                if _M3U8_HINT in url:
                    if captured["url"] is None:
                        captured["url"] = url
                    # We only need the URL, not the playlist bytes.
                    return route.abort()
                if request.resource_type in _BLOCKED_TYPES or url.endswith(".ts"):
                    return route.abort()
                return route.continue_()

            context.route("**/*", route_handler)
            page = context.new_page()
            try:
                page.goto(kwik_url, wait_until="domcontentloaded", timeout=45000)
                # Poll briefly for the player to request its playlist.
                for _ in range(16):
                    if captured["url"]:
                        break
                    page.wait_for_timeout(500)
                if not captured["url"]:
                    from app.animepahe import _extract_m3u8_from_kwik_page

                    fallback = _extract_m3u8_from_kwik_page(page.content())
                    if fallback:
                        captured["url"] = fallback
            except Exception as error:
                print(f"[kwik] browser attempt {attempt}: {str(error)[:90]}", flush=True)
            finally:
                context.close()

            if captured["url"]:
                return captured["url"]
        return None

    def fetch(self, kwik_url: str) -> str | None:
        self._ensure_worker()
        holder: dict[str, str | None] = {}
        done = threading.Event()
        self._queue.put((kwik_url, holder, done))
        if not done.wait(timeout=170):
            print("[kwik] browser fetch timed out", flush=True)
            return None
        return holder.get("m3u8")


BROWSER_KWIK = _BrowserKwikFetcher()
