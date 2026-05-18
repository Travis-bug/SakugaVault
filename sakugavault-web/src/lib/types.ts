export interface CurrentUserDto {
  id: string;
  displayName: string;
  userName: string;
  email: string;
}

export interface AuthResponseDto {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  user: CurrentUserDto;
}

export interface RegisterRequestDto {
  displayName: string;
  userName: string;
  email: string;
  password: string;
}

export interface LoginRequestDto {
  identifier: string;
  password: string;
}

export interface CatalogHeroDto {
  id: string;
  title: string;
  synopsis: string;
  posterImageUrl: string;
  backdropImageUrl: string;
  watchRoute: string;
}

export interface AnimeCardDto {
  id: string;
  title: string;
  coverImageUrl: string;
  episodeCount: number;
  subAvailable: boolean;
  dubAvailable: boolean;
  watchRoute: string;
}

export interface GenreRailDto {
  genre: string;
  titles: AnimeCardDto[];
}

export interface HomeCatalogDto {
  heroBanner: CatalogHeroDto;
  genreRows: GenreRailDto[];
}

export interface SearchAnimeResultDto {
  id: string;
  title: string;
  synopsis: string;
  posterImageUrl: string;
  episodeCount: number;
  subAvailable: boolean;
  dubAvailable: boolean;
  watchRoute: string;
  genres: string[];
}

export interface CatalogSearchResponseDto {
  query: string;
  totalResults: number;
  results: SearchAnimeResultDto[];
}

export interface CommentDto {
  userName: string;
  body: string;
  postedAtUtc: string;
}

export interface CommentPostedDto {
  commentId: string;
  animeId: string;
  authorDisplayName: string;
  body: string;
  createdAtUtc: string;
}

export interface PlaybackDescriptorDto {
  preferredProtocol: string;
  resolveOnPlay: boolean;
  resolverMode: string;
}

export interface WatchEpisodeDto {
  episodeNumber: number;
  label: string;
}

export interface WatchSeasonDto {
  id: string;
  label: string;
  episodes: WatchEpisodeDto[];
}

export interface WatchPageDto {
  id: string;
  title: string;
  synopsis: string;
  posterImageUrl: string;
  backdropImageUrl: string;
  runtimeMinutes: number;
  episodeCount: number;
  subAvailable: boolean;
  dubAvailable: boolean;
  metadataLastSyncedAtUtc: string | null;
  playback: PlaybackDescriptorDto;
  seasons: WatchSeasonDto[];
  comments: CommentDto[];
  similarAnime: AnimeCardDto[];
}

export interface ResolvedPlaybackDto {
  animeId: string;
  episodeNumber: number;
  isResolved: boolean;
  preferredProtocol: string;
  streamUrl: string | null;
  sourceHost: string | null;
  usedFallback: boolean;
  statusMessage: string;
}

export interface WatchHistoryEntryDto {
  animeId: string;
  animeTitle: string;
  posterImageUrl: string;
  episodeNumber: number;
  positionSeconds: number;
  durationSeconds: number;
  completed: boolean;
  lastWatchedAtUtc: string;
}

export interface CursorPagedResult<T> {
  items: T[];
  nextCursor: string | null;
  pageSize: number;
  hasMore: boolean;
}

export interface MetadataSyncResultDto {
  animeId: string;
  provider: string;
  syncedAtUtc: string;
  wasUpdated: boolean;
  statusMessage: string;
}

export interface DownloadQueueItemDto {
  downloadId: string;
  animeId: string;
  animeTitle: string;
  posterImageUrl: string;
  episodeNumber: number;
  preferredLanguage: string;
  quality: string;
  status: string;
  requestedAtUtc: string;
  watchRoute: string;
}

export interface ProfileSummaryDto {
  user: CurrentUserDto;
  continueWatchingCount: number;
  completedEntriesCount: number;
  commentsCount: number;
  queuedDownloadsCount: number;
  recentHistory: WatchHistoryEntryDto[];
  downloadQueuePreview: DownloadQueueItemDto[];
}
