import type { GenreRailDto } from "../lib/types";
import { MediaCard } from "./MediaCard";

export function GenreRail({ rail }: { rail: GenreRailDto }) {
  return (
    <section className="rail reveal">
      <div className="rail__header">
        <div>
          <span className="eyebrow">Signal Lane</span>
          <h2>{rail.genre}</h2>
        </div>
        <span className="rail__count">{rail.titles.length} picks</span>
      </div>
      <div className="rail__row">
        {rail.titles.map((anime, index) => (
          <MediaCard key={`${rail.genre}-${anime.id}`} anime={anime} rank={index + 1} />
        ))}
      </div>
    </section>
  );
}
