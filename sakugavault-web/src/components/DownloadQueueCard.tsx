import { Link } from "react-router-dom";
import type { DownloadQueueItemDto } from "../lib/types";

interface DownloadQueueCardProps {
  item: DownloadQueueItemDto;
  actionLabel?: string;
  onAction?: (downloadId: string) => void | Promise<void>;
  isBusy?: boolean;
}

export function DownloadQueueCard({
  item,
  actionLabel = "Remove",
  onAction,
  isBusy = false
}: DownloadQueueCardProps) {
  return (
    <article className="queue-card">
      <img src={item.posterImageUrl} alt={item.animeTitle} className="queue-card__image" />
      <div className="queue-card__body">
        <div className="queue-card__headline">
          <div>
            <span className="eyebrow">Queue Item</span>
            <h3>{item.animeTitle}</h3>
          </div>
          <span className="queue-pill">{item.status}</span>
        </div>
        <div className="queue-card__meta">
          <span>Episode {item.episodeNumber}</span>
          <span>{item.preferredLanguage.toUpperCase()}</span>
          <span>{item.quality}</span>
        </div>
        <div className="queue-card__actions">
          <Link to={item.watchRoute} className="button button--ghost">
            Open Watch Page
          </Link>
          {onAction ? (
            <button
              type="button"
              className="button"
              onClick={() => void onAction(item.downloadId)}
              disabled={isBusy}
            >
              {isBusy ? "Working..." : actionLabel}
            </button>
          ) : null}
        </div>
      </div>
    </article>
  );
}
