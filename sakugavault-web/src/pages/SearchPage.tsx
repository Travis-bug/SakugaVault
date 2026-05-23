import { useEffect, useState } from "react";
import { AppChrome } from "../components/AppChrome";
import { EmptyState } from "../components/EmptyState";
import { LoadingPanel } from "../components/LoadingPanel";
import { SearchResultCard } from "../components/SearchResultCard";
import { useAuth } from "../auth/AuthContext";
import { isApiError } from "../lib/api";
import type { CatalogSearchResponseDto } from "../lib/types";

export function SearchPage() {
  const { apiRequest } = useAuth();
  const [query, setQuery] = useState("");
  const [payload, setPayload] = useState<CatalogSearchResponseDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    const timeout = window.setTimeout(() => {
      void (async () => {
        try {
          const searchParams = new URLSearchParams();
          searchParams.set("limit", "24");
          if (query.trim().length > 0) {
            searchParams.set("q", query.trim());
          }

          const result = await apiRequest<CatalogSearchResponseDto>(`/api/catalog/search?${searchParams.toString()}`, {
            signal: controller.signal
          });

          setPayload(result);
          setErrorMessage(null);
        } catch (error) {
          if ((error as Error).name === "AbortError") {
            return;
          }

          if (isApiError(error)) {
            setErrorMessage(error.message);
          } else if (error instanceof Error) {
            setErrorMessage(error.message);
          } else {
            setErrorMessage("Search failed.");
          }
        } finally {
          setIsLoading(false);
        }
      })();
    }, 260);

    return () => {
      controller.abort();
      window.clearTimeout(timeout);
    };
  }, [apiRequest, query]);

  if (isLoading && !payload) {
    return <LoadingPanel title="Loading Search" message="Searching the current providers for trending and matching titles." />;
  }

  return (
    <AppChrome
      eyebrow="Search"
      title="Search"
      description="Search by title or browse what's trending right now."
      showMasthead={false}
    >
      <section className="search-panel reveal">
        <label className="search-panel__field">
          <input
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder="Search by title, or genre"
            aria-label="Search by title or genre"
          />
          <button type="button" className="button button--primary" onClick={() => setQuery("")}>
            Clear
          </button>
        </label>

        <div className="search-panel__summary">
          <strong>{payload?.totalResults ?? 0}</strong>
          <span>{query.trim().length > 0 ? "matching titles" : "trending titles"}</span>
        </div>
      </section>

      {errorMessage ? (
        <EmptyState title="Search Failed" message={errorMessage} />
      ) : payload && payload.results.length > 0 ? (
        <section className="search-results">
          {payload.results.map((result) => (
            <SearchResultCard key={result.id} result={result} />
          ))}
        </section>
      ) : (
        <EmptyState
          title="No Results"
          message="Try a broader title, a shorter keyword, or come back in a moment if the providers are catching up."
        />
      )}
    </AppChrome>
  );
}
