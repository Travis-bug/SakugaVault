import { Link } from "react-router-dom";
import type { AnimeCardDto } from "../lib/types";

export function MediaCard({ anime, rank }: { anime: AnimeCardDto; rank?: number }) {
  return (
    <Link to={anime.watchRoute} className="media-card">
      {rank ? <span className="media-card__rank">{String(rank).padStart(2, "0")}</span> : null}
      <img
        src={anime.coverImageUrl}
        alt={anime.title}
        className="media-card__image"
        loading="lazy"
        decoding="async"
      />
      <div className="media-card__scrim" />
      <div className="media-card__overlay">
        <div className="media-card__copy">
          <h3>{anime.title}</h3>
          <p>{anime.episodeCount} episodes ready</p>
        </div>
        <div className="media-card__flags">
          {anime.subAvailable ? <span>SUB</span> : null}
          {anime.dubAvailable ? <span>DUB</span> : null}
        </div>
      </div>
    </Link>
  );
}
