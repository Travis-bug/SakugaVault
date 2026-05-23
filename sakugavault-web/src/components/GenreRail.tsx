import { useEffect, useRef, useState } from "react";
import type { GenreRailDto } from "../lib/types";
import { MediaCard } from "./MediaCard";

export function GenreRail({ rail }: { rail: GenreRailDto }) {
  const rowRef = useRef<HTMLDivElement | null>(null);
  const [canScrollBackward, setCanScrollBackward] = useState(false);
  const [canScrollForward, setCanScrollForward] = useState(false);

  useEffect(() => {
    const row = rowRef.current;
    if (row === null) {
      return undefined;
    }

    const railRow = row;

    function updateScrollState() {
      const maxScrollLeft = railRow.scrollWidth - railRow.clientWidth;
      setCanScrollBackward(railRow.scrollLeft > 4);
      setCanScrollForward(maxScrollLeft > 4 && railRow.scrollLeft < maxScrollLeft - 4);
    }

    updateScrollState();
    railRow.addEventListener("scroll", updateScrollState, { passive: true });
    window.addEventListener("resize", updateScrollState);

    return () => {
      railRow.removeEventListener("scroll", updateScrollState);
      window.removeEventListener("resize", updateScrollState);
    };
  }, [rail.titles.length]);

  function scrollRail(direction: -1 | 1) {
    const row = rowRef.current;
    if (!row) {
      return;
    }

    row.scrollBy({
      left: direction * Math.max(row.clientWidth * 0.82, 240),
      behavior: "smooth"
    });
  }

  return (
    <section className="rail reveal">
      <div className="rail__header">
        <div>
          <h2>{rail.genre}</h2>
        </div>
        <span className="rail__count">{rail.titles.length} picks</span>
      </div>
      <div className="rail__viewport">
        {rail.titles.length > 0 ? (
          <>
            <button
              type="button"
              className="rail__control rail__control--prev"
              aria-label={`Scroll ${rail.genre} titles backward`}
              onClick={() => scrollRail(-1)}
              disabled={!canScrollBackward}
            >
              <span aria-hidden="true">‹</span>
            </button>
            <button
              type="button"
              className="rail__control rail__control--next"
              aria-label={`Scroll ${rail.genre} titles forward`}
              onClick={() => scrollRail(1)}
              disabled={!canScrollForward}
            >
              <span aria-hidden="true">›</span>
            </button>
          </>
        ) : null}
        <div ref={rowRef} className="rail__row" data-count={rail.titles.length}>
          {rail.titles.map((anime, index) => (
            <MediaCard key={`${rail.genre}-${anime.id}`} anime={anime} rank={index + 1} />
          ))}
        </div>
      </div>
    </section>
  );
}
