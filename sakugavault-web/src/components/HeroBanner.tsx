import { Link } from "react-router-dom";
import type { CatalogHeroDto } from "../lib/types";

export function HeroBanner({ hero }: { hero: CatalogHeroDto }) {
  const heroTitle = hero.title.trim().length > 0 ? hero.title : "Awaiting Live Signal";
  const heroSynopsis = hero.synopsis.trim().length > 0
    ? hero.synopsis
    : "Once the live catalog locks onto an upstream title, the featured frame will appear here.";
  const backdropImage = hero.backdropImageUrl.trim().length > 0 ? hero.backdropImageUrl : hero.posterImageUrl;
  const hasWatchRoute = hero.watchRoute.trim().length > 0;
  const hasPosterImage = hero.posterImageUrl.trim().length > 0;

  return (
    <section
      className="hero-banner reveal"
      style={{ backgroundImage: `linear-gradient(135deg, rgba(6, 9, 14, 0.86), rgba(17, 11, 9, 0.52)), url(${backdropImage})` }}
    >
      <div className="hero-banner__veil" />
      <div className="hero-banner__content">
        <div className="hero-banner__eyebrow-row">
          <span className="eyebrow">Featured Signal</span>
          <div className="hero-banner__chips">
            <span className="hero-chip hero-chip--live">Live Feed</span>
            <span className="hero-chip">Warm Tech</span>
          </div>
        </div>
        <h2>{heroTitle}</h2>
        <p>{heroSynopsis}</p>
        <div className="hero-banner__actions">
          {hasWatchRoute ? (
            <Link to={hero.watchRoute} className="button">
              Watch Now
            </Link>
          ) : (
            <span className="button button--disabled" aria-disabled="true">
              Awaiting Stream
            </span>
          )}
          <a href="#catalog-rails" className="button button--ghost">
            Browse Rails
          </a>
        </div>
        <div className="hero-banner__pulse-track" aria-hidden="true">
          <span className="is-live" />
          <span />
          <span />
          <span />
          <span />
        </div>
      </div>
      {hasPosterImage ? (
        <div className="hero-banner__poster">
          <img src={hero.posterImageUrl} alt={heroTitle} loading="eager" decoding="async" />
        </div>
      ) : null}
    </section>
  );
}
