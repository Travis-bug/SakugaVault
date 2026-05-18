import { FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { useParams } from "react-router-dom";
import { AppChrome } from "../components/AppChrome";
import { EmptyState } from "../components/EmptyState";
import { LoadingPanel } from "../components/LoadingPanel";
import { MediaCard } from "../components/MediaCard";
import { useAuth } from "../auth/AuthContext";
import { isApiError } from "../lib/api";
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

function formatSyncDate(value: string | null) {
  if (!value) {
    return "Never synced";
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short"
  }).format(new Date(value));
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
  const [preferredLanguage, setPreferredLanguage] = useState<"sub" | "dub">("sub");
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
            preferredLanguage
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
  }, [animeId, apiRequest, preferredLanguage, selectedEpisodeNumber, watchPage]);

  useEffect(() => {
    const streamUrl = resolvedPlayback?.streamUrl;
    if (!streamUrl || !videoRef.current) {
      return;
    }

    const activeStreamUrl = streamUrl;
    const video = videoRef.current;
    let cancelled = false;
    let hlsInstance: HlsInstance | null = null;

    async function attachStream() {
      if (video.canPlayType("application/vnd.apple.mpegurl")) {
        video.src = activeStreamUrl;
        try {
          await video.play();
        } catch {
          // Browsers may block autoplay until the user interacts.
        }
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
        try {
          await videoRef.current?.play();
        } catch {
          // Browsers may block autoplay until the user interacts.
        }
      });
    }

    void attachStream();

    return () => {
      cancelled = true;

      if (hlsInstance) {
        hlsInstance.destroy();
        return;
      }

      video.pause();
      video.removeAttribute("src");
      video.load();
    };
  }, [resolvedPlayback?.streamUrl]);

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
            />
            {!resolvedPlayback?.streamUrl ? (
              <div className="player-frame__overlay">
                <span className="eyebrow">{isResolving ? "Loading Stream" : "Playback Resolver"}</span>
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
                  <span className="eyebrow">Season Browser</span>
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
                Language
                <select
                  value={preferredLanguage}
                  onChange={(event) => setPreferredLanguage(event.target.value as "sub" | "dub")}
                >
                  <option value="sub">Sub</option>
                  <option value="dub" disabled={!watchPage.dubAvailable}>
                    Dub
                  </option>
                </select>
              </label>
              {statusMessage ? <p className="status-message">{statusMessage}</p> : null}
            </div>
          </div>
        </div>

        <aside className="watch-stage__meta">
          <span className="eyebrow">Metadata Deck</span>
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
              <dt>Sub</dt>
              <dd>{watchPage.subAvailable ? "Available" : "Unavailable"}</dd>
            </div>
            <div>
              <dt>Dub</dt>
              <dd>{watchPage.dubAvailable ? "Available" : "Unavailable"}</dd>
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
                {resolvedPlayback.sourceHost ?? "Unknown host"}
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
              <span className="eyebrow">Discussion</span>
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
