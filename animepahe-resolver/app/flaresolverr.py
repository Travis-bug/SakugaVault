"""Thin FlareSolverr client with a reused, self-healing browser session.

FlareSolverr runs headless Chrome and solves the Cloudflare challenge on the
AnimePahe site shell. We keep ONE long-lived session (the cf_clearance cookie
lasts a while), so only the first request pays the ~15-30s solve cost and the
rest are fast navigations. On any failure we drop the session and recreate it.

FlareSolverr returns the fetched body wrapped in an HTML <pre>; for the JSON API
endpoints we strip tags and unescape entities to recover the raw JSON.
"""

from __future__ import annotations

import html
import json
import re
import threading
import time

import requests

from app.config import CONFIG


class FlareSolverrError(RuntimeError):
    pass


class FlareSolverr:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._session: str | None = None

    def _command(self, payload: dict) -> dict:
        try:
            response = requests.post(
                f"{CONFIG.flaresolverr_url}/v1",
                json=payload,
                timeout=(CONFIG.flaresolverr_timeout_ms / 1000) + 15,
            )
        except requests.RequestException as error:
            raise FlareSolverrError(f"FlareSolverr is unreachable: {error}") from error

        if response.status_code != 200:
            raise FlareSolverrError(
                f"FlareSolverr returned HTTP {response.status_code}: {response.text[:200]}"
            )
        data = response.json()
        if data.get("status") != "ok":
            raise FlareSolverrError(data.get("message", "FlareSolverr command failed."))
        return data

    def _ensure_session(self) -> str:
        if self._session is not None:
            return self._session
        created = self._command({"cmd": "sessions.create"})
        self._session = created["session"]
        # Prime the session by clearing Cloudflare on the site shell so the
        # cf_clearance cookie is banked before the first real request.
        self._command(
            {
                "cmd": "request.get",
                "url": CONFIG.animepahe_bases[0] + "/",
                "session": self._session,
                "maxTimeout": CONFIG.flaresolverr_timeout_ms,
            }
        )
        return self._session

    def _reset_session(self) -> None:
        old, self._session = self._session, None
        if old:
            try:
                self._command({"cmd": "sessions.destroy", "session": old})
            except FlareSolverrError:
                pass

    def warm(self) -> None:
        """Pre-solve the challenge at startup so the first user request is fast."""
        with self._lock:
            try:
                self._ensure_session()
            except FlareSolverrError:
                self._reset_session()
                raise

    def start_keepalive(self, interval_seconds: int, ping_url: str) -> None:
        """Periodically touch the target so Cloudflare clearance stays fresh.

        Without this, clearance expires during idle gaps and the next user
        request pays the full challenge solve (10-30s, occasionally a timeout).
        The heartbeat moves that cost off the user path; get() already
        re-solves on a stale session, so a failed ping just self-heals.
        """
        if interval_seconds <= 0:
            return

        def loop() -> None:
            while True:
                time.sleep(interval_seconds)
                try:
                    self.get(ping_url)
                except FlareSolverrError:
                    pass  # get() already dropped the session; next call re-solves.

        threading.Thread(target=loop, daemon=True, name="flaresolverr-keepalive").start()

    def get(self, url: str) -> str:
        """Fetches a URL through the cleared browser session; returns response body."""
        with self._lock:
            for attempt in (1, 2):
                try:
                    session = self._ensure_session()
                    result = self._command(
                        {
                            "cmd": "request.get",
                            "url": url,
                            "session": session,
                            "maxTimeout": CONFIG.flaresolverr_timeout_ms,
                        }
                    )
                    return result.get("solution", {}).get("response", "")
                except FlareSolverrError:
                    # A stale session or transient solve failure: drop it and retry once.
                    self._reset_session()
                    if attempt == 2:
                        raise
        raise FlareSolverrError("FlareSolverr request failed.")

    def get_json(self, url: str):
        """Fetches a URL whose body is JSON wrapped in FlareSolverr's <pre>."""
        body = self.get(url)
        text = html.unescape(re.sub(r"<[^>]+>", "", body))
        match = re.search(r"(\{.*\}|\[.*\])", text, re.DOTALL)
        if not match:
            raise FlareSolverrError("Expected JSON but found none in the response.")
        try:
            return json.loads(match.group(1))
        except json.JSONDecodeError as error:
            raise FlareSolverrError(f"Could not parse JSON from response: {error}") from error


FLARESOLVERR = FlareSolverr()
