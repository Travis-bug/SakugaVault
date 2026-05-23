import { FormEvent, useEffect, useState } from "react";
import { AppChrome } from "../components/AppChrome";
import { DownloadQueueCard } from "../components/DownloadQueueCard";
import { EmptyState } from "../components/EmptyState";
import { LoadingPanel } from "../components/LoadingPanel";
import { SearchResultCard } from "../components/SearchResultCard";
import { useAuth } from "../auth/AuthContext";
import { isApiError } from "../lib/api";
import type { CatalogSearchResponseDto, DownloadQueueItemDto } from "../lib/types";

export function DownloadsPage() {
  const { apiRequest } = useAuth();
  const [queue, setQueue] = useState<DownloadQueueItemDto[]>([]);
  const [searchQuery, setSearchQuery] = useState("");
  const [searchResults, setSearchResults] = useState<CatalogSearchResponseDto | null>(null);
  const [draftEpisodeByAnime, setDraftEpisodeByAnime] = useState<Record<string, number>>({});
  const [draftLanguageByAnime, setDraftLanguageByAnime] = useState<Record<string, string>>({});
  const [isLoading, setIsLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    async function loadQueue() {
      try {
        const payload = await apiRequest<DownloadQueueItemDto[]>("/api/downloads/me", {
          signal: controller.signal
        });
        setQueue(payload);
      } catch (error) {
        if ((error as Error).name === "AbortError") {
          return;
        }

        if (isApiError(error)) {
          setStatusMessage(error.message);
        } else if (error instanceof Error) {
          setStatusMessage(error.message);
        } else {
          setStatusMessage("Download queue failed to load.");
        }
      } finally {
        setIsLoading(false);
      }
    }

    void loadQueue();
    return () => controller.abort();
  }, [apiRequest]);

  useEffect(() => {
    const controller = new AbortController();
    const timeout = window.setTimeout(() => {
      if (searchQuery.trim().length < 2) {
        setSearchResults(null);
        return;
      }

      void (async () => {
        try {
          const result = await apiRequest<CatalogSearchResponseDto>(
            `/api/catalog/search?q=${encodeURIComponent(searchQuery.trim())}&limit=8`,
            { signal: controller.signal }
          );
          setSearchResults(result);
        } catch (error) {
          if ((error as Error).name === "AbortError") {
            return;
          }

          if (isApiError(error)) {
            setStatusMessage(error.message);
          } else if (error instanceof Error) {
            setStatusMessage(error.message);
          } else {
            setStatusMessage("Anime lookup failed.");
          }
        }
      })();
    }, 260);

    return () => {
      controller.abort();
      window.clearTimeout(timeout);
    };
  }, [apiRequest, searchQuery]);

  async function queueDownload(animeId: string, defaultLanguage: string) {
    setBusyId(animeId);
    setStatusMessage(null);

    try {
      const payload = await apiRequest<DownloadQueueItemDto>("/api/downloads", {
        method: "POST",
        body: {
          animeId,
          episodeNumber: draftEpisodeByAnime[animeId] ?? 1,
          preferredLanguage: draftLanguageByAnime[animeId] ?? defaultLanguage,
          quality: "1080p"
        }
      });

      setQueue((current) => [payload, ...current]);
      setStatusMessage("Episode queued for download.");
    } catch (error) {
      if (isApiError(error)) {
        setStatusMessage(error.message);
      } else if (error instanceof Error) {
        setStatusMessage(error.message);
      } else {
        setStatusMessage("Queue request failed.");
      }
    } finally {
      setBusyId(null);
    }
  }

  async function removeDownload(downloadId: string) {
    setBusyId(downloadId);
    setStatusMessage(null);

    try {
      await apiRequest<void>(`/api/downloads/${downloadId}`, {
        method: "DELETE"
      });
      setQueue((current) => current.filter((item) => item.downloadId !== downloadId));
      setStatusMessage("Download removed from queue.");
    } catch (error) {
      if (isApiError(error)) {
        setStatusMessage(error.message);
      } else if (error instanceof Error) {
        setStatusMessage(error.message);
      } else {
        setStatusMessage("Queue removal failed.");
      }
    } finally {
      setBusyId(null);
    }
  }

  if (isLoading) {
    return <LoadingPanel title="Loading Downloads" message="Loading your offline queue and quick-add search." />;
  }

  return (
    <AppChrome
      eyebrow="Downloads"
      title="Offline Queue"
      description="Save episodes here for offline handling later."
      showMasthead={false}
    >
      <section className="downloads-grid">
        <section className="downloads-search reveal">
          <h2>Queue an episode</h2>
          <p>Search for a title, choose an episode and language, then add it to your queue.</p>
          <label className="search-panel__field">
            <input
              value={searchQuery}
              onChange={(event) => setSearchQuery(event.target.value)}
              placeholder="Search titles to queue..."
              aria-label="Search titles to queue"
            />
          </label>

          {searchResults?.results.length ? (
            <div className="downloads-search__results">
              {searchResults.results.map((result) => {
                const selectedLanguage =
                  draftLanguageByAnime[result.id] ?? (result.subAvailable ? "sub" : "dub");

                return (
                  <div key={result.id} className="downloads-search__entry">
                    <SearchResultCard
                      result={result}
                      actionArea={
                        <button
                          type="button"
                          className="button"
                          onClick={() => void queueDownload(result.id, selectedLanguage)}
                          disabled={busyId === result.id}
                        >
                          {busyId === result.id ? "Queueing..." : "Queue Download"}
                        </button>
                      }
                    />
                    <form
                      className="downloads-inline-form"
                      onSubmit={(event: FormEvent<HTMLFormElement>) => {
                        event.preventDefault();
                        void queueDownload(result.id, selectedLanguage);
                      }}
                    >
                      <label>
                        Episode
                        <input
                          type="number"
                          min={1}
                          max={result.episodeCount}
                          value={draftEpisodeByAnime[result.id] ?? 1}
                          onChange={(event) =>
                            setDraftEpisodeByAnime((current) => ({
                              ...current,
                              [result.id]: Number(event.target.value)
                            }))
                          }
                        />
                      </label>
                      <label>
                        Language
                        <select
                          value={selectedLanguage}
                          onChange={(event) =>
                            setDraftLanguageByAnime((current) => ({
                              ...current,
                              [result.id]: event.target.value
                            }))
                          }
                        >
                          {result.subAvailable ? <option value="sub">Sub</option> : null}
                          {result.dubAvailable ? <option value="dub">Dub</option> : null}
                        </select>
                      </label>
                    </form>
                  </div>
                );
              })}
            </div>
          ) : searchQuery.trim().length >= 2 ? (
            <EmptyState title="No Titles Found" message="Try a different title fragment or search more broadly." />
          ) : null}
        </section>

        <section className="downloads-queue reveal">
          <div className="rail__header">
            <h2>{queue.length} queued item{queue.length === 1 ? "" : "s"}</h2>
          </div>
          {statusMessage ? <p className="status-message">{statusMessage}</p> : null}
          {queue.length > 0 ? (
            <div className="downloads-queue__list">
              {queue.map((item) => (
                <DownloadQueueCard
                  key={item.downloadId}
                  item={item}
                  onAction={removeDownload}
                  isBusy={busyId === item.downloadId}
                />
              ))}
            </div>
          ) : (
            <EmptyState
              title="Queue Is Empty"
              message="Search for a title on this screen and add the episode you want to keep offline later."
            />
          )}
        </section>
      </section>
    </AppChrome>
  );
}
