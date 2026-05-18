# SakugaVault Frontend File Guide

This document explains the first React client scaffold in `sakugavault-web/`.

## High-level shape

- `package.json`
  - Declares the React, React Router, and HLS.js dependencies plus the Vite scripts.
- `vite.config.ts`
  - Runs the client on port `5173` and proxies `/api` plus `/health` to the ASP.NET backend.
  - `VITE_PROXY_TARGET` lets Docker point the Vite dev server at the `api` container instead of `localhost`.
- `index.html`
  - Single entry document for the SPA.
- `Dockerfile`
  - Container entry for running the Vite dev server through Docker Compose.

## Source layout

- `src/main.tsx`
  - Boots the React tree and wraps the app in `BrowserRouter` and `AuthProvider`.
- `src/App.tsx`
  - Owns the route table:
    - `/login`
    - `/`
    - `/watch/:animeId`
    - `/search`
    - `/downloads`
    - `/profile`
- `src/styles.css`
  - Global visual system for the app, including the cinematic layout, gradients, panel styles, and responsive behavior.

## Auth and API plumbing

- `src/auth/AuthContext.tsx`
  - Session source of truth for the SPA.
  - Stores the short-lived access token and current user in browser storage.
  - The refresh token stays in an `HttpOnly` cookie so reloads can silently restore the session without exposing the refresh token to JavaScript.
  - Handles token refresh automatically when a protected request sees an expired token or a `401`.
- `src/lib/api.ts`
  - Low-level typed `fetch` wrapper and `ApiError` contract.
- `src/lib/config.ts`
  - Reads `VITE_API_BASE_URL` and resolves API paths.
- `src/lib/types.ts`
  - TypeScript equivalents of the backend DTOs so the client matches the ASP.NET contracts exactly.

## UI components

- `src/components/AppChrome.tsx`
  - Shared authenticated shell with the top bar, vault branding, and sidebar toggle.
- `src/components/Sidebar.tsx`
  - Hidden slide-out navigation for catalog, search, downloads, profile, and logout.
- `src/components/HeroBanner.tsx`
  - Home-page hero banner fed by `GET /api/catalog/home`.
- `src/components/GenreRail.tsx`
  - Horizontal row renderer for genre-grouped anime cards.
- `src/components/MediaCard.tsx`
  - Shared anime tile used in rails and similar-anime grids.
- `src/components/SearchResultCard.tsx`
  - Larger discovery card used on the search and downloads pages.
- `src/components/DownloadQueueCard.tsx`
  - Shared queue item renderer used on downloads and profile.
- `src/components/StatCard.tsx`
  - Compact metric card for the profile dashboard.
- `src/components/ProtectedRoute.tsx`
  - Route gate that redirects unauthenticated users to `/login`.
- `src/components/LoadingPanel.tsx`
  - Shared loading state screen.
- `src/components/EmptyState.tsx`
  - Shared empty/error fallback panel.

## Pages

- `src/pages/LoginPage.tsx`
  - Combined login and registration gateway wired to:
    - `POST /api/auth/login`
    - `POST /api/auth/register`
- `src/pages/CatalogPage.tsx`
  - Loads:
    - `GET /api/catalog/home`
    - `GET /api/watch/history/me?pageSize=8`
  - Renders the hero banner, continue-watching strip, and genre rails.
- `src/pages/WatchPage.tsx`
  - Loads `GET /api/watch/{animeId}`.
  - Resolves streams through `POST /api/watch/{animeId}/resolve-playback`.
  - Saves playback progress through `POST /api/watch/history`.
  - Posts comments through `POST /api/catalog/comments`.
  - Queues episode downloads through `POST /api/downloads`.
  - Uses HLS.js to attach `.m3u8` streams to the video element when the backend returns a playable source.
- `src/pages/SearchPage.tsx`
  - Calls `GET /api/catalog/search`.
  - Blank queries render a trending discovery fallback.
- `src/pages/DownloadsPage.tsx`
  - Calls:
    - `GET /api/downloads/me`
    - `POST /api/downloads`
    - `DELETE /api/downloads/{downloadId}`
  - Includes quick-add lookup using `GET /api/catalog/search`.
- `src/pages/ProfilePage.tsx`
  - Calls `GET /api/profile/me`.
  - Renders identity, usage stats, recent history, and queued-download preview.

## Current frontend scope

What is real now:
- Auth session storage and refresh flow.
- Protected routing.
- Catalog rendering from live provider-backed API data.
- Search rendering from live provider-backed API data.
- Persisted download queue management.
- Profile dashboard from live API data.
- Watch-page rendering from live API data.
- Playback resolution call path.
- Watch-history upserts.
- Comment posting.
- Metadata sync trigger from the watch page.

What is still scaffolded:
- Advanced player analytics and controls.
- Developer/admin import and metadata management screens.
- Actual background download workers or file-transfer infrastructure.
