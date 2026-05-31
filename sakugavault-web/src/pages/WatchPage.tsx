import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { useParams } from "react-router-dom";
import { AppChrome } from "../components/AppChrome";
import { EmptyState } from "../components/EmptyState";
import { LoadingPanel } from "../components/LoadingPanel";
import { MediaCard } from "../components/MediaCard";
import { useAuth } from "../auth/AuthContext";
import { isApiError } from "../lib/api";
import { resolveApiUrl } from "../lib/config";
import type {
  CommentDto,
  CommentPostedDto,
  DownloadQueueItemDto,
  ResolvedPlaybackDto,
  WatchHistoryEntryDto,
  WatchPageDto,
  WatchSeasonDto
} from "../lib/types";

type WatchTab = "comments" | "similar";
type HlsInstance = import("hls.js").default;
type AudioLanguage = "en" | "ja";
type SubtitleLanguage = "en" | "ja" | "off";
type MediaTrackLike = { lang?: string; language?: string; label?: string; name?: string };

const AUDIO_LANGUAGE_STORAGE_KEY = "sakugavault.watch.audioLanguage";
const SUBTITLE_LANGUAGE_STORAGE_KEY = "sakugavault.watch.subtitleLanguage";

const languageLabels: Record<AudioLanguage | SubtitleLanguage, string> = {
  en: "English",
  ja: "Japanese",
  off: "Off"
};

function readStoredAudioLanguage(): AudioLanguage {
  if (typeof window === "undefined") {
    return "en";
  }

  return normalizeAudioLanguage(window.localStorage.getItem(AUDIO_LANGUAGE_STORAGE_KEY)) ?? "en";
}

function readStoredSubtitleLanguage(): SubtitleLanguage {
  if (typeof window === "undefined") {
    return "en";
  }

  return normalizeSubtitleLanguage(window.localStorage.getItem(SUBTITLE_LANGUAGE_STORAGE_KEY)) ?? "en";
}

function normalizeAudioLanguage(value: string | null | undefined): AudioLanguage | null {
  const normalized = normalizeLanguageCode(value);
  return normalized === "en" || normalized === "ja" ? normalized : null;
}

function normalizeSubtitleLanguage(value: string | null | undefined): SubtitleLanguage | null {
  const normalized = normalizeLanguageCode(value);
  return normalized === "en" || normalized === "ja" || normalized === "off" ? normalized : null;
}

function normalizeLanguageCode(value: string | null | undefined) {
  const normalized = value?.trim().toLowerCase();
  if (!normalized) {
    return "";
  }

  const baseLanguage = normalized.split("-")[0];
  switch (baseLanguage) {
    case "eng":
    case "english":
      return "en";
    case "jpn":
    case "jp":
    case "japanese":
      return "ja";
    case "none":
    case "false":
    case "disabled":
      return "off";
    default:
      return baseLanguage;
  }
}

function uniqueLanguages<T extends string>(languages: T[]) {
  return [...new Set(languages)];
}

function getAudioLanguageOptions(payload: WatchPageDto | null): AudioLanguage[] {
  const metadataLanguages = payload?.availableAudioLanguages
    ?.map(normalizeAudioLanguage)
    .filter((language): language is AudioLanguage => Boolean(language)) ?? [];

  const fallbackLanguages: AudioLanguage[] = [];
  if (payload?.dubAvailable) {
    fallbackLanguages.push("en");
  }

  if (payload?.subAvailable) {
    fallbackLanguages.push("ja");
  }

  return uniqueLanguages([...metadataLanguages, ...fallbackLanguages]).length > 0
    ? uniqueLanguages([...metadataLanguages, ...fallbackLanguages])
    : ["ja"];
}

function getSubtitleLanguageOptions(payload: WatchPageDto | null): SubtitleLanguage[] {
  const metadataLanguages = payload?.availableSubtitleLanguages
    ?.map(normalizeSubtitleLanguage)
    .filter((language): language is SubtitleLanguage => Boolean(language) && language !== "off") ?? [];
  const fallbackLanguages: SubtitleLanguage[] = payload?.subAvailable ? ["en"] : [];

  return uniqueLanguages([...metadataLanguages, ...fallbackLanguages, "off"]);
}

function pickAvailableLanguage<T extends string>(current: T, available: T[], fallbacks: T[]) {
  if (available.includes(current)) {
    return current;
  }

  return fallbacks.find((language) => available.includes(language)) ?? available[0] ?? current;
}

function getTrackLanguage(track: MediaTrackLike) {
  return normalizeLanguageCode(track.lang ?? track.language ?? track.label ?? track.name);
}

function findTrackIndex(tracks: MediaTrackLike[], preferred: string, fallbacks: string[]) {
  const preferredLanguages = uniqueLanguages([preferred, ...fallbacks].filter(Boolean));

  for (const language of preferredLanguages) {
    const index = tracks.findIndex((track) => getTrackLanguage(track) === language);
    if (index >= 0) {
      return index;
    }
  }

  return -1;
}

function applyHlsLanguagePreferences(
  hlsInstance: HlsInstance,
  audioLanguage: AudioLanguage,
  subtitleLanguage: SubtitleLanguage
) {
  const audioTracks = (hlsInstance.audioTracks ?? []) as MediaTrackLike[];
  const audioTrack = findTrackIndex(audioTracks, audioLanguage, audioLanguage === "en" ? ["ja"] : ["en"]);
  if (audioTrack >= 0) {
    hlsInstance.audioTrack = audioTrack;
  }

  const subtitleTracks = (hlsInstance.subtitleTracks ?? []) as MediaTrackLike[];
  if (subtitleLanguage === "off") {
    hlsInstance.subtitleTrack = -1;
    return;
  }

  const subtitleTrack = findTrackIndex(subtitleTracks, subtitleLanguage, subtitleLanguage === "en" ? ["ja"] : ["en"]);
  hlsInstance.subtitleTrack = subtitleTrack;
}

function applyNativeTextTrackPreferences(video: HTMLVideoElement, subtitleLanguage: SubtitleLanguage) {
  Array.from(video.textTracks).forEach((track) => {
    if (subtitleLanguage === "off") {
      track.mode = "disabled";
      return;
    }

    const normalizedLanguage = normalizeLanguageCode(track.language || track.label);
    track.mode = normalizedLanguage === subtitleLanguage ? "showing" : "disabled";
  });
}

function formatLanguageList(languages: string[]) {
  return languages.length > 0
    ? languages.map((language) => languageLabels[(normalizeLanguageCode(language) || language) as AudioLanguage | SubtitleLanguage] ?? language).join(", ")
    : "Unavailable";
}

function formatSyncDate(value: string | null) {
  if (!value) {
    return "Never synced";
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(value));
}

function isHlsStream(streamUrl: string, preferredProtocol?: string) {
  return preferredProtocol?.toUpperCase() === "HLS" || /\.m3u8(?:$|\?)/i.test(streamUrl);
}

function resolveStreamUrl(streamUrl: string) {
  return streamUrl.startsWith("/api/") ? resolveApiUrl(streamUrl) : streamUrl;
}

function resolveSubtitleTrackLanguage(language: string) {
  return normalizeLanguageCode(language) || language;
}

async function playWhenAllowed(video: HTMLVideoElement) {
  try {
    await video.play();
  } catch {
    // Browsers may block autoplay until the user interacts.
  }
}

function getInitialSelection(payload: WatchPageDto) {
  const firstSeasonWithEpisodes = payload.seasons.find((season) => season.episodes.length > 0) ?? payload.seasons[0] ?? null;
  const firstEpisode = firstSeasonWithEpisodes?.episodes[0] ?? null;

  return {
    seasonId: firstSeasonWithEpisodes?.id ?? null,
    episodeNumber: firstEpisode?.episodeNumber ?? (payload.episodeCount > 0 ? 1 : null)
  };
}

export function WatchPage() {
  const { animeId } = useParams();
  const { apiRequest, user } = useAuth();
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const lastSavedSecondRef = useRef(0);
  const [watchPage, setWatchPage] = useState<WatchPageDto | null>(null);
  const [comments, setComments] = useState<CommentDto[]>([]);
  const [resolvedPlayback, setResolvedPlayback] = useState<ResolvedPlaybackDto | null>(null);
  const [selectedSeasonId, setSelectedSeasonId] = useState<string | null>(null);
  const [selectedEpisodeNumber, setSelectedEpisodeNumber] = useState<number | null>(null);
  const [audioLanguage, setAudioLanguage] = useState<AudioLanguage>(readStoredAudioLanguage);
  const [subtitleLanguage, setSubtitleLanguage] = useState<SubtitleLanguage>(readStoredSubtitleLanguage);
  const [allowRegionalFallback, setAllowRegionalFallback] = useState(false);
  const [activeTab, setActiveTab] = useState<WatchTab>("comments");
  const [commentBody, setCommentBody] = useState("");
  const [isLoading, setIsLoading] = useState(true);
  const [isResolving, setIsResolving] = useState(false);
  const [isPostingComment, setIsPostingComment] = useState(false);
  const [isQueueingDownload, setIsQueueingDownload] = useState(false);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    async function loadWatchPage() {
      if (!animeId) {
        setErrorMessage("Missing anime identifier.");
        setIsLoading(false);
        return;
      }

      try {
        const payload = await apiRequest<WatchPageDto>(`/api/watch/${animeId}`, {
          signal: controller.signal
        });

        const selection = getInitialSelection(payload);
        setWatchPage(payload);
        setComments(payload.comments);
        setSelectedSeasonId(selection.seasonId);
        setSelectedEpisodeNumber(selection.episodeNumber);
        setResolvedPlayback(null);
        setStatusMessage(selection.episodeNumber
          ? `Loading episode ${selection.episodeNumber}.`
          : "Episode details are not available for this title yet.");
      } catch (error) {
        if ((error as Error).name === "AbortError") {
          return;
        }

        if (isApiError(error)) {
          setErrorMessage(error.message);
        } else if (error instanceof Error) {
          setErrorMessage(error.message);
        } else {
          setErrorMessage("The watch page failed to load.");
        }
      } finally {
        setIsLoading(false);
      }
    }

    void loadWatchPage();
    return () => controller.abort();
  }, [animeId, apiRequest]);

  const selectedSeason = useMemo<WatchSeasonDto | null>(() => {
    if (!watchPage) {
      return null;
    }

    return watchPage.seasons.find((season) => season.id === selectedSeasonId) ?? watchPage.seasons[0] ?? null;
  }, [watchPage, selectedSeasonId]);

  const audioLanguageOptions = useMemo(() => getAudioLanguageOptions(watchPage), [watchPage]);
  const subtitleLanguageOptions = useMemo(() => getSubtitleLanguageOptions(watchPage), [watchPage]);
  const effectiveAudioLanguage = watchPage
    ? pickAvailableLanguage(audioLanguage, audioLanguageOptions, ["en", "ja"])
    : audioLanguage;
  const effectiveSubtitleLanguage = watchPage
    ? pickAvailableLanguage(subtitleLanguage, subtitleLanguageOptions, ["en", "ja", "off"])
    : subtitleLanguage;
  const preferredLanguage: "sub" | "dub" = effectiveAudioLanguage === "en" ? "dub" : "sub";

  useEffect(() => {
    if (!watchPage) {
      return;
    }

    const nextAudioLanguage = pickAvailableLanguage(audioLanguage, audioLanguageOptions, ["en", "ja"]);
    if (nextAudioLanguage !== audioLanguage) {
      setAudioLanguage(nextAudioLanguage);
    }
  }, [audioLanguage, audioLanguageOptions, watchPage]);

  useEffect(() => {
    if (!watchPage) {
      return;
    }

    const nextSubtitleLanguage = pickAvailableLanguage(subtitleLanguage, subtitleLanguageOptions, ["en", "ja", "off"]);
    if (nextSubtitleLanguage !== subtitleLanguage) {
      setSubtitleLanguage(nextSubtitleLanguage);
    }
  }, [subtitleLanguage, subtitleLanguageOptions, watchPage]);

  useEffect(() => {
    window.localStorage.setItem(AUDIO_LANGUAGE_STORAGE_KEY, audioLanguage);
  }, [audioLanguage]);

  useEffect(() => {
    window.localStorage.setItem(SUBTITLE_LANGUAGE_STORAGE_KEY, subtitleLanguage);
  }, [subtitleLanguage]);

  useEffect(() => {
    if (!animeId || !watchPage || selectedEpisodeNumber === null) {
      return;
    }

    const controller = new AbortController();

    async function resolvePlayback() {
      setIsResolving(true);
      setResolvedPlayback(null);
      setStatusMessage(`Loading episode ${selectedEpisodeNumber}...`);
      lastSavedSecondRef.current = 0;

      try {
        const response = await apiRequest<ResolvedPlaybackDto>(`/api/watch/${animeId}/resolve-playback`, {
          method: "POST",
          body: {
            episodeNumber: selectedEpisodeNumber,
            preferredLanguage,
            audioLanguage: effectiveAudioLanguage,
            subtitleLanguage: effectiveSubtitleLanguage,
            allowRegionalFallback
          },
          signal: controller.signal
        });

        setResolvedPlayback(response);
        setStatusMessage(response.statusMessage);
      } catch (error) {
        if ((error as Error).name === "AbortError") {
          return;
        }

        if (isApiError(error)) {
          setStatusMessage(error.message);
        } else if (error instanceof Error) {
          setStatusMessage(error.message);
        } else {
          setStatusMessage("Playback resolution failed.");
        }
      } finally {
        if (!controller.signal.aborted) {
          setIsResolving(false);
        }
      }
    }

    void resolvePlayback();
    return () => controller.abort();
  }, [allowRegionalFallback, animeId, apiRequest, effectiveAudioLanguage, effectiveSubtitleLanguage, preferredLanguage, selectedEpisodeNumber, watchPage]);

  useEffect(() => {
    const streamUrl = resolvedPlayback?.streamUrl;
    if (!streamUrl || !videoRef.current) {
      return;
    }

    const activeStreamUrl = resolveStreamUrl(streamUrl);
    const activeProtocol = resolvedPlayback?.preferredProtocol;
    const video = videoRef.current;
    let cancelled = false;
    let hlsInstance: HlsInstance | null = null;

    async function attachStream() {
      if (!isHlsStream(activeStreamUrl, activeProtocol)) {
        video.src = activeStreamUrl;
        video.load();
        applyNativeTextTrackPreferences(video, effectiveSubtitleLanguage);
        await playWhenAllowed(video);
        return;
      }

      if (video.canPlayType("application/vnd.apple.mpegurl")) {
        video.src = activeStreamUrl;
        video.load();
        applyNativeTextTrackPreferences(video, effectiveSubtitleLanguage);
        await playWhenAllowed(video);
        return;
      }

      const hlsModule = await import("hls.js");
      const Hls = hlsModule.default;

      if (cancelled || !videoRef.current) {
        return;
      }

      if (!Hls.isSupported()) {
        setStatusMessage("This browser cannot play HLS streams directly.");
        return;
      }

      hlsInstance = new Hls();
      hlsInstance.loadSource(activeStreamUrl);
      hlsInstance.attachMedia(videoRef.current);
      hlsInstance.on(Hls.Events.MANIFEST_PARSED, async () => {
        if (hlsInstance) {
          applyHlsLanguagePreferences(hlsInstance, effectiveAudioLanguage, effectiveSubtitleLanguage);
        }

        const currentVideo = videoRef.current;
        if (currentVideo) {
          await playWhenAllowed(currentVideo);
        }
      });
    }

    void attachStream();

    return () => {
      cancelled = true;

      if (hlsInstance) {
        hlsInstance.destroy();
      }

      video.pause();
      video.removeAttribute("src");
      video.load();
    };
  }, [effectiveAudioLanguage, effectiveSubtitleLanguage, resolvedPlayback?.preferredProtocol, resolvedPlayback?.streamUrl]);

  async function saveHistory(positionSeconds: number, durationSeconds: number, completed: boolean) {
    if (!animeId || selectedEpisodeNumber === null) {
      return;
    }

    try {
      await apiRequest<WatchHistoryEntryDto>("/api/watch/history", {
        method: "POST",
        body: {
          animeId,
          episodeNumber: selectedEpisodeNumber,
          positionSeconds: Math.max(0, Math.floor(positionSeconds)),
          durationSeconds: Math.max(0, Math.floor(durationSeconds)),
          completed
        }
      });
    } catch {
      // Playback telemetry should not interrupt the viewing session.
    }
  }

  function handleTimeUpdate() {
    const video = videoRef.current;
    if (!video) {
      return;
    }

    const currentSecond = Math.floor(video.currentTime);
    if (currentSecond - lastSavedSecondRef.current < 15) {
      return;
    }

    lastSavedSecondRef.current = currentSecond;
    void saveHistory(video.currentTime, video.duration || 0, false);
  }

  function handlePause() {
    const video = videoRef.current;
    if (!video) {
      return;
    }

    void saveHistory(video.currentTime, video.duration || 0, false);
  }

  function handleEnded() {
    const video = videoRef.current;
    if (!video) {
      return;
    }

    void saveHistory(video.duration || 0, video.duration || 0, true);
  }

  async function handlePostComment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!animeId || commentBody.trim().length === 0) {
      return;
    }

    setIsPostingComment(true);

    try {
      const response = await apiRequest<CommentPostedDto>("/api/catalog/comments", {
        method: "POST",
        body: {
          animeId,
          body: commentBody.trim()
        }
      });

      setComments((current) => [
        {
          userName: response.authorDisplayName,
          body: response.body,
          postedAtUtc: response.createdAtUtc
        },
        ...current
      ]);
      setCommentBody("");
      setActiveTab("comments");
    } catch (error) {
      if (isApiError(error)) {
        setStatusMessage(error.message);
      } else if (error instanceof Error) {
        setStatusMessage(error.message);
      } else {
        setStatusMessage("Comment posting failed.");
      }
    } finally {
      setIsPostingComment(false);
    }
  }

  async function handleQueueDownload() {
    if (!animeId || selectedEpisodeNumber === null) {
      return;
    }

    setIsQueueingDownload(true);

    try {
      await apiRequest<DownloadQueueItemDto>("/api/downloads", {
        method: "POST",
        body: {
          animeId,
          episodeNumber: selectedEpisodeNumber,
          preferredLanguage,
          quality: "1080p"
        }
      });
      setStatusMessage(`Episode ${selectedEpisodeNumber} queued for download.`);
    } catch (error) {
      if (isApiError(error)) {
        setStatusMessage(error.message);
      } else if (error instanceof Error) {
        setStatusMessage(error.message);
      } else {
        setStatusMessage("Download queue request failed.");
      }
    } finally {
      setIsQueueingDownload(false);
    }
  }

  function handleSelectSeason(season: WatchSeasonDto) {
    setSelectedSeasonId(season.id);
    const firstEpisode = season.episodes[0];
    if (firstEpisode) {
      setSelectedEpisodeNumber(firstEpisode.episodeNumber);
    }
  }

  if (isLoading) {
    return <LoadingPanel title="Loading Watch Page" message="Loading episode details, comments, and playback options." />;
  }

  if (!watchPage) {
    return (
      <AppChrome
        eyebrow="Watch"
        title="Unavailable"
        description="This title could not be loaded right now."
      >
        <EmptyState title="Watch Page Unavailable" message={errorMessage ?? "No watch payload was returned."} />
      </AppChrome>
    );
  }

  return (
    <AppChrome
      eyebrow="Watch"
      title={watchPage.title}
      description="Choose a season, pick an episode, and start playback as soon as a source is ready."
      actions={
        <button type="button" className="button button--ghost" onClick={handleQueueDownload} disabled={isQueueingDownload || selectedEpisodeNumber === null}>
          {isQueueingDownload ? "Queueing..." : "Queue Download"}
        </button>
      }
    >
      <section
        className="watch-stage reveal"
        style={{ backgroundImage: `linear-gradient(145deg, rgba(10, 8, 20, 0.92), rgba(24, 11, 6, 0.55)), url(${watchPage.backdropImageUrl})` }}
      >
        <div className="watch-stage__player">
          <div className="player-frame">
            <video
              ref={videoRef}
              className="player-frame__video"
              controls
              playsInline
              autoPlay
              poster={watchPage.posterImageUrl}
              onTimeUpdate={handleTimeUpdate}
              onPause={handlePause}
              onEnded={handleEnded}
            >
              {resolvedPlayback?.subtitles?.map((subtitle) => {
                const subtitleLanguageCode = resolveSubtitleTrackLanguage(subtitle.language);
                return (
                  <track
                    key={`${subtitle.language}-${subtitle.url}`}
                    kind={subtitle.kind || "subtitles"}
                    src={resolveStreamUrl(subtitle.url)}
                    srcLang={subtitleLanguageCode}
                    label={subtitle.label}
                    default={normalizeSubtitleLanguage(subtitle.language) === effectiveSubtitleLanguage}
                  />
                );
              })}
            </video>
            {!resolvedPlayback?.streamUrl ? (
              <div className="player-frame__overlay">
                <h2>{isResolving ? "Preparing Episode" : "Episode Source Pending"}</h2>
                <p>
                  {selectedEpisodeNumber
                    ? `Episode ${selectedEpisodeNumber} is being prepared.`
                    : "Episode details are still loading for this title."}
                </p>
              </div>
            ) : null}
          </div>

          <div className="watch-stage__controls watch-stage__controls--stacked">
            <div className="episode-browser">
              <div className="episode-browser__header">
                <div>
                  <h3>Season and Episode Library</h3>
                </div>
                {selectedEpisodeNumber ? <span className="queue-pill">Episode {selectedEpisodeNumber}</span> : null}
              </div>

              {watchPage.seasons.length > 0 ? (
                <>
                  <div className="episode-browser__seasons">
                    {watchPage.seasons.map((season) => (
                      <button
                        key={season.id}
                        type="button"
                        className={`episode-browser__season ${season.id === selectedSeason?.id ? "is-active" : ""}`}
                        onClick={() => handleSelectSeason(season)}
                      >
                        {season.label}
                      </button>
                    ))}
                  </div>
                  <div className="episode-browser__episodes">
                    {selectedSeason?.episodes.map((episode) => (
                      <button
                        key={`${selectedSeason.id}-${episode.episodeNumber}`}
                        type="button"
                        className={`episode-browser__episode ${episode.episodeNumber === selectedEpisodeNumber ? "is-active" : ""}`}
                        onClick={() => setSelectedEpisodeNumber(episode.episodeNumber)}
                      >
                        {episode.label}
                      </button>
                    ))}
                  </div>
                </>
              ) : (
                <EmptyState
                  title="Episode Library Unavailable"
                  message="The current providers did not return an episode list for this title."
                />
              )}
            </div>

            <div className="watch-stage__control-bar">
              <label>
                Audio
                <select
                  value={effectiveAudioLanguage}
                  onChange={(event) => setAudioLanguage(event.target.value as AudioLanguage)}
                >
                  {audioLanguageOptions.map((language) => (
                    <option key={language} value={language}>
                      {languageLabels[language]}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Subtitles
                <select
                  value={effectiveSubtitleLanguage}
                  onChange={(event) => setSubtitleLanguage(event.target.value as SubtitleLanguage)}
                >
                  {subtitleLanguageOptions.map((language) => (
                    <option key={language} value={language}>
                      {languageLabels[language]}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Fallback
                <select
                  value={allowRegionalFallback ? "regional" : "strict"}
                  onChange={(event) => setAllowRegionalFallback(event.target.value === "regional")}
                >
                  <option value="strict">English/Japanese only</option>
                  <option value="regional">Any playable</option>
                </select>
              </label>
              {statusMessage ? <p className="status-message">{statusMessage}</p> : null}
            </div>
          </div>
        </div>

        <aside className="watch-stage__meta">
          <h2>{watchPage.title}</h2>
          <p>{watchPage.synopsis}</p>
          <dl className="meta-grid">
            <div>
              <dt>Runtime</dt>
              <dd>{watchPage.runtimeMinutes} min</dd>
            </div>
            <div>
              <dt>Episodes</dt>
              <dd>{watchPage.episodeCount}</dd>
            </div>
            <div>
              <dt>Audio</dt>
              <dd>{formatLanguageList(audioLanguageOptions)}</dd>
            </div>
            <div>
              <dt>Subtitles</dt>
              <dd>{formatLanguageList(subtitleLanguageOptions.filter((language) => language !== "off"))}</dd>
            </div>
            <div>
              <dt>Resolver</dt>
              <dd>{watchPage.playback.resolverMode}</dd>
            </div>
            <div>
              <dt>Last Sync</dt>
              <dd>{formatSyncDate(watchPage.metadataLastSyncedAtUtc)}</dd>
            </div>
          </dl>
          {resolvedPlayback ? (
            <div className="meta-callout">
              <strong>{resolvedPlayback.isResolved ? "Stream ready" : "Resolution incomplete"}</strong>
              <span>
                {[resolvedPlayback.resolver, resolvedPlayback.sourceHost ?? "Unknown host"].filter(Boolean).join(" / ")}
                {resolvedPlayback.audioLanguage ? ` / audio ${languageLabels[normalizeAudioLanguage(resolvedPlayback.audioLanguage) ?? "ja"]}` : ""}
                {resolvedPlayback.subtitleLanguage && resolvedPlayback.subtitleLanguage !== "off"
                  ? ` / subtitles ${languageLabels[normalizeSubtitleLanguage(resolvedPlayback.subtitleLanguage) ?? "en"]}`
                  : ""}
                {resolvedPlayback.usedFallback ? " via fallback provider" : ""}
              </span>
            </div>
          ) : null}
        </aside>
      </section>

      <section className="watch-tabs reveal">
        <div className="watch-tabs__controls">
          <button
            type="button"
            className={`watch-tabs__button ${activeTab === "comments" ? "is-active" : ""}`}
            onClick={() => setActiveTab("comments")}
          >
            Comments
          </button>
          <button
            type="button"
            className={`watch-tabs__button ${activeTab === "similar" ? "is-active" : ""}`}
            onClick={() => setActiveTab("similar")}
          >
            Similar Anime
          </button>
        </div>

        {activeTab === "comments" ? (
          <div className="comments-grid">
            <form className="comment-form" onSubmit={handlePostComment}>
              <h2>Drop a reaction</h2>
              <textarea
                value={commentBody}
                onChange={(event) => setCommentBody(event.target.value)}
                maxLength={2000}
                placeholder={`What stood out to you, ${user?.displayName ?? "viewer"}?`}
                required
              />
              <button type="submit" className="button" disabled={isPostingComment}>
                {isPostingComment ? "Posting..." : "Post Comment"}
              </button>
            </form>

            <div className="comment-feed">
              {comments.length > 0 ? (
                comments.map((comment) => (
                  <article key={`${comment.userName}-${comment.postedAtUtc}-${comment.body.slice(0, 16)}`} className="comment-card">
                    <header>
                      <strong>{comment.userName}</strong>
                      <span>{formatSyncDate(comment.postedAtUtc)}</span>
                    </header>
                    <p>{comment.body}</p>
                  </article>
                ))
              ) : (
                <EmptyState
                  title="No Comments Yet"
                  message="Be the first to leave a reaction for this episode."
                />
              )}
            </div>
          </div>
        ) : (
          <div className="similar-grid">
            {watchPage.similarAnime.length > 0 ? (
              watchPage.similarAnime.map((anime) => <MediaCard key={anime.id} anime={anime} />)
            ) : (
              <EmptyState
                title="No Similar Titles Right Now"
                message="Related picks are not available for this title yet."
              />
            )}
          </div>
        )}
      </section>
    </AppChrome>
  );
}
