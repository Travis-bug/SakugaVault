import { AppChrome } from "../components/AppChrome";
import { DownloadQueueCard } from "../components/DownloadQueueCard";
import { EmptyState } from "../components/EmptyState";
import { LoadingPanel } from "../components/LoadingPanel";
import { StatCard } from "../components/StatCard";
import { useAuth } from "../auth/AuthContext";
import { isApiError } from "../lib/api";
import type { ProfileSummaryDto } from "../lib/types";
import { useEffect, useState } from "react";
import { Link } from "react-router-dom";

export function ProfilePage() {
  const { apiRequest } = useAuth();
  const [profile, setProfile] = useState<ProfileSummaryDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    async function loadProfile() {
      try {
        const payload = await apiRequest<ProfileSummaryDto>("/api/profile/me", {
          signal: controller.signal
        });
        setProfile(payload);
      } catch (error) {
        if ((error as Error).name === "AbortError") {
          return;
        }

        if (isApiError(error)) {
          setErrorMessage(error.message);
        } else if (error instanceof Error) {
          setErrorMessage(error.message);
        } else {
          setErrorMessage("Profile loading failed.");
        }
      } finally {
        setIsLoading(false);
      }
    }

    void loadProfile();
    return () => controller.abort();
  }, [apiRequest]);

  if (isLoading) {
    return <LoadingPanel title="Loading Profile" message="Loading your account, watch progress, and queue summary." />;
  }

  if (!profile) {
    return (
      <AppChrome
        eyebrow="Profile"
        title="Identity Deck"
        description="Your profile could not be loaded right now."
      >
        <EmptyState title="Profile Unavailable" message={errorMessage ?? "No profile data was returned."} />
      </AppChrome>
    );
  }

  return (
    <AppChrome
      eyebrow="Profile"
      title={profile.user.displayName}
      description={`Signed in as @${profile.user.userName}. Here's your recent activity and saved queue.`}
    >
      <section className="profile-hero reveal">
        <div className="profile-hero__identity">
          <span className="eyebrow">Current User</span>
          <h2>{profile.user.displayName}</h2>
          <p>@{profile.user.userName}</p>
          <p>{profile.user.email}</p>
        </div>
        <div className="profile-stats">
          <StatCard label="Continue Watching" value={profile.continueWatchingCount} />
          <StatCard label="Completed Entries" value={profile.completedEntriesCount} />
          <StatCard label="Comments" value={profile.commentsCount} />
          <StatCard label="Queued Downloads" value={profile.queuedDownloadsCount} />
        </div>
      </section>

      <section className="profile-sections">
        <section className="profile-panel reveal">
          <div className="rail__header">
            <span className="eyebrow">Recent Watch History</span>
            <h2>Resume Lane</h2>
          </div>
          {profile.recentHistory.length > 0 ? (
            <div className="profile-history">
              {profile.recentHistory.map((entry) => (
                <Link key={`${entry.animeId}-${entry.episodeNumber}`} to={`/watch/${entry.animeId}`} className="profile-history__item">
                  <img src={entry.posterImageUrl} alt={entry.animeTitle} />
                  <div>
                    <strong>{entry.animeTitle}</strong>
                    <p>Episode {entry.episodeNumber}</p>
                    <span>{entry.completed ? "Completed" : `${entry.positionSeconds}s saved`}</span>
                  </div>
                </Link>
              ))}
            </div>
          ) : (
            <EmptyState title="No Watch History Yet" message="Once you start playback, recent progress will appear here." />
          )}
        </section>

        <section className="profile-panel reveal">
          <div className="rail__header">
            <span className="eyebrow">Download Preview</span>
            <h2>Queue Snapshot</h2>
          </div>
          {profile.downloadQueuePreview.length > 0 ? (
            <div className="downloads-queue__list">
              {profile.downloadQueuePreview.map((item) => (
                <DownloadQueueCard key={item.downloadId} item={item} />
              ))}
            </div>
          ) : (
            <EmptyState
              title="No Download Queue Yet"
              message="Use the downloads screen to create your first queued episode request."
            />
          )}
        </section>
      </section>
    </AppChrome>
  );
}
