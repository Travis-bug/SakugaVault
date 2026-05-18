import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { AppChrome } from "../components/AppChrome";
import { EmptyState } from "../components/EmptyState";
import { GenreRail } from "../components/GenreRail";
import { HeroBanner } from "../components/HeroBanner";
import { LoadingPanel } from "../components/LoadingPanel";
import { useAuth } from "../auth/AuthContext";
import { isApiError } from "../lib/api";
import type { CursorPagedResult, HomeCatalogDto, WatchHistoryEntryDto } from "../lib/types";

function formatProgress(entry: WatchHistoryEntryDto) {
  if (entry.completed) {
    return "Completed";
  }

  if (entry.durationSeconds <= 0) {
    return `${entry.positionSeconds}s watched`;
  }

  const progress = Math.min(100, Math.round((entry.positionSeconds / entry.durationSeconds) * 100));
  return `${progress}% watched`;
}

export function CatalogPage() {
  const { apiRequest, user } = useAuth();
  const [catalog, setCatalog] = useState<HomeCatalogDto | null>(null);
  const [history, setHistory] = useState<CursorPagedResult<WatchHistoryEntryDto> | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    async function loadCatalog() {
      try {
        const [homeCatalog, watchHistory] = await Promise.all([
          apiRequest<HomeCatalogDto>("/api/catalog/home", { signal: controller.signal }),
          apiRequest<CursorPagedResult<WatchHistoryEntryDto>>("/api/watch/history/me?pageSize=8", {
            signal: controller.signal
          })
        ]);

        setCatalog(homeCatalog);
        setHistory(watchHistory);
      } catch (error) {
        if ((error as Error).name === "AbortError") {
          return;
        }

        if (isApiError(error)) {
          setErrorMessage(error.message);
        } else if (error instanceof Error) {
          setErrorMessage(error.message);
        } else {
          setErrorMessage("The catalog failed to load.");
        }
      } finally {
        setIsLoading(false);
      }
    }

    void loadCatalog();
    return () => controller.abort();
  }, []);

  if (isLoading) {
    return <LoadingPanel title="Loading Catalog" message="Loading today's featured titles and continue-watching picks." />;
  }

  if (!catalog) {
    return (
      <AppChrome
        eyebrow="Catalog"
        title="Home"
        description="No catalog data was returned for this request."
      >
        <EmptyState title="Catalog Offline" message={errorMessage ?? "No catalog data was returned."} />
      </AppChrome>
    );
  }

  return (
    <AppChrome
      eyebrow="Catalog"
      title="Home"
      description={`Welcome back, ${user?.displayName ?? "viewer"}. Pick up where you left off or jump into something new.`}
    >
      <HeroBanner hero={catalog.heroBanner} />

      <section className="history-strip reveal">
        <div className="rail__header">
          <span className="eyebrow">Resume Queue</span>
          <h2>Continue Watching</h2>
        </div>
        {history && history.items.length > 0 ? (
          <div className="history-strip__row">
            {history.items.map((entry) => (
              <Link key={`${entry.animeId}-${entry.episodeNumber}`} to={`/watch/${entry.animeId}`} className="history-card">
                <img src={entry.posterImageUrl} alt={entry.animeTitle} />
                <div className="history-card__body">
                  <h3>{entry.animeTitle}</h3>
                  <p>Episode {entry.episodeNumber}</p>
                  <span>{formatProgress(entry)}</span>
                </div>
              </Link>
            ))}
          </div>
        ) : (
          <EmptyState
            title="No Continue-Watching Yet"
            message="Once the watch page saves playback progress, titles will stack here for quick resume access."
          />
        )}
      </section>

      <section id="catalog-rails" className="catalog-rails">
        {catalog.genreRows.length === 0 ? (
          <EmptyState
            title="No Titles Available Right Now"
            message="The current providers did not return a home catalog. Try again in a moment."
          />
        ) : (
          catalog.genreRows.map((rail) => <GenreRail key={rail.genre} rail={rail} />)
        )}
      </section>
    </AppChrome>
  );
}
