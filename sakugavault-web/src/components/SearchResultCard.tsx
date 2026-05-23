import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import type { SearchAnimeResultDto } from "../lib/types";

interface SearchResultCardProps {
  result: SearchAnimeResultDto;
  actionArea?: ReactNode;
}

export function SearchResultCard({ result, actionArea }: SearchResultCardProps) {
  const synopsis = result.synopsis.trim();
  const hasSynopsis = synopsis.length > 0 && !synopsis.toLowerCase().startsWith("live title loaded from provider");

  return (
    <article className="search-card">
      <img
        src={result.posterImageUrl}
        alt={result.title}
        className="search-card__image"
        loading="lazy"
        decoding="async"
      />
      <div className="search-card__body">
        <div className="search-card__header">
          <div>
            <h3 title={result.title}>{result.title}</h3>
          </div>
          <div className="media-card__flags">
            {result.subAvailable ? <span>SUB</span> : null}
            {result.dubAvailable ? <span>DUB</span> : null}
          </div>
        </div>
        {hasSynopsis ? <p>{synopsis}</p> : null}
        <div className="search-card__chips">
          {result.genres.map((genre) => (
            <span key={`${result.id}-${genre}`} className="queue-pill">
              {genre}
            </span>
          ))}
        </div>
        <div className="search-card__actions">
          <span>{result.episodeCount} episodes</span>
          <div className="search-card__buttons">
            <Link to={result.watchRoute} className="button button--ghost">
              Watch
            </Link>
            {actionArea}
          </div>
        </div>
      </div>
    </article>
  );
}
