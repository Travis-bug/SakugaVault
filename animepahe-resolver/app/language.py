"""Language normalization + the foreign-language interceptor.

Mirrors crawlee-scrapingbee-service/src/language.ts and the C#
StreamScraperService so candidates from this resolver rank consistently. The
old spec's "subLanguage: 'invalid'" idea becomes classify_foreign(): hardcoded
non-EN/JA content is flagged so the watch handler emits
languageSource='hardcoded' instead of silently serving it.
"""

from __future__ import annotations

import unicodedata
from dataclasses import dataclass
from typing import Literal


def _strip_diacritics(value: str) -> str:
    return "".join(ch for ch in unicodedata.normalize("NFD", value) if not unicodedata.combining(ch))


def normalize_language_code(value: str | None) -> str:
    normalized = (value or "").strip().lower()
    if not normalized:
        return ""

    compact = _strip_diacritics(normalized).replace("_", "-").replace(" ", "-")

    if "english" in compact:
        return "en"
    if "japanese" in compact:
        return "ja"
    if "spanish" in compact or "espanol" in compact or "castellano" in compact:
        return "es"
    if "portuguese" in compact or "portugues" in compact:
        return "pt"

    base = compact.split("-")[0]
    return {
        "eng": "en",
        "english": "en",
        "jpn": "ja",
        "jp": "ja",
        "japanese": "ja",
        "spa": "es",
        "es": "es",
        "esp": "es",
        "por": "pt",
        "pt": "pt",
        "ptbr": "pt",
        "none": "off",
        "false": "off",
        "disabled": "off",
    }.get(base, base)


def normalize_subtitle_language(value: str | None, audio_language: Literal["en", "ja"] = "ja") -> str:
    normalized = normalize_language_code(value)
    if normalized in ("en", "ja", "off"):
        return normalized
    return "en" if audio_language == "ja" else "off"


@dataclass(frozen=True)
class LanguagePlan:
    preferred_language: Literal["sub", "dub"]
    audio_language: Literal["en", "ja"]
    subtitle_language: str


def build_language_plan(
    preferred_language: str | None,
    audio_language: str | None,
    subtitle_language: str | None,
) -> LanguagePlan:
    normalized_audio = normalize_language_code(audio_language)
    if normalized_audio == "en":
        audio: Literal["en", "ja"] = "en"
    elif normalized_audio == "ja":
        audio = "ja"
    else:
        audio = "en" if (preferred_language or "").strip().lower() == "dub" else "ja"

    return LanguagePlan(
        preferred_language="dub" if audio == "en" else "sub",
        audio_language=audio,
        subtitle_language=normalize_subtitle_language(subtitle_language, audio),
    )


def is_foreign_language(code: str) -> bool:
    return code not in ("", "en", "ja", "off")


_FOREIGN_MARKERS = (
    "spanish", "espanol", "castellano", "latino",
    "portuguese", "portugues", "legendado", "dublado",
    "french", "francais", "german", "deutsch",
    "italian", "italiano", "multi-sub", "multi sub", "multisub", "multi-audio",
)


def classify_foreign(text: str | None) -> str | None:
    """Returns a warning string when content looks hardcoded non-EN/JA, else None."""
    haystack = _strip_diacritics(text or "").lower()
    for marker in _FOREIGN_MARKERS:
        if marker in haystack:
            return (
                f'Source appears to carry hardcoded "{marker}" language tracks '
                "rather than verified English/Japanese."
            )
    return None
