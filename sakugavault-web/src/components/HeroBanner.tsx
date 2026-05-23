import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { CatalogHeroDto } from "../lib/types";

const placeholderHero: CatalogHeroDto = {
  id: "",
  title: "",
  synopsis: "",
  posterImageUrl: "",
  backdropImageUrl: "",
  watchRoute: ""
};

export function HeroBanner({ heroItems }: { heroItems: CatalogHeroDto[] }) {
  const items = heroItems.length > 0 ? heroItems : [placeholderHero];
  const [activeIndex, setActiveIndex] = useState(0);
  const [isPaused, setIsPaused] = useState(false);

  useEffect(() => {
    setActiveIndex((current) => (current >= items.length ? 0 : current));
  }, [items.length]);

  useEffect(() => {
    if (items.length <= 1 || isPaused) {
      return undefined;
    }

    const intervalId = window.setInterval(() => {
      setActiveIndex((current) => (current + 1) % items.length);
    }, 6000);

    return () => window.clearInterval(intervalId);
  }, [isPaused, items.length]);

  function showPreviousSlide() {
    setActiveIndex((current) => (current - 1 + items.length) % items.length);
  }

  function showNextSlide() {
    setActiveIndex((current) => (current + 1) % items.length);
  }

  return (
    <section
      className="hero-banner reveal"
      onMouseEnter={() => setIsPaused(true)}
      onMouseLeave={() => setIsPaused(false)}
    >
      {items.map((hero, index) => {
        const heroTitle = hero.title.trim().length > 0 ? hero.title : "Awaiting Live Signal";
        const heroSynopsis = hero.synopsis.trim();
        const hasSynopsis = heroSynopsis.length > 0 && !heroSynopsis.toLowerCase().startsWith("live title loaded from provider");
        const backdropImage = hero.backdropImageUrl.trim().length > 0 ? hero.backdropImageUrl : hero.posterImageUrl;
        const hasWatchRoute = hero.watchRoute.trim().length > 0;
        const hasPosterImage = hero.posterImageUrl.trim().length > 0;
        const isActive = index === activeIndex;

        return (
          <article
            key={hero.id || `hero-${index}`}
            className={`hero-banner__slide ${isActive ? "is-active" : ""}`}
            aria-hidden={!isActive}
          >
            {backdropImage ? (
              <img
                src={backdropImage}
                alt=""
                aria-hidden="true"
                className="hero-banner__backdrop"
                loading={index === 0 ? "eager" : "lazy"}
                decoding="async"
              />
            ) : null}
            <div className="hero-banner__veil" />
            <div className="hero-banner__content">
              <h2 title={heroTitle}>{heroTitle}</h2>
              {hasSynopsis ? <p>{heroSynopsis}</p> : null}
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
            </div>
            {hasPosterImage ? (
              <div className="hero-banner__poster">
                <img src={hero.posterImageUrl} alt={heroTitle} loading={index === 0 ? "eager" : "lazy"} decoding="async" />
              </div>
            ) : null}
          </article>
        );
      })}

      {items.length > 1 ? (
        <>
          <button
            type="button"
            className="hero-banner__arrow hero-banner__arrow--prev"
            aria-label="Show previous featured title"
            onClick={showPreviousSlide}
          >
            <span aria-hidden="true">‹</span>
          </button>
          <button
            type="button"
            className="hero-banner__arrow hero-banner__arrow--next"
            aria-label="Show next featured title"
            onClick={showNextSlide}
          >
            <span aria-hidden="true">›</span>
          </button>
        </>
      ) : null}

      {items.length > 1 ? (
        <div className="hero-banner__dots" aria-label="Hero slide navigation">
          {items.map((hero, index) => {
            const title = hero.title.trim().length > 0 ? hero.title : `Hero slide ${index + 1}`;

            return (
              <button
                key={hero.id || `hero-dot-${index}`}
                type="button"
                className={`hero-banner__dot ${index === activeIndex ? "is-active" : ""}`}
                aria-label={`Show ${title}`}
                aria-pressed={index === activeIndex}
                onClick={() => setActiveIndex(index)}
              />
            );
          })}
        </div>
      ) : null}
    </section>
  );
}
