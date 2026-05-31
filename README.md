# SakugaVault

## Project Overview

SakugaVault is a free, open-source, and self-hostable anime streaming platform designed to bypass corporate paywalls and monopolies. The platform aims to provide a premium, ad-free viewing experience while keeping the core repository legally clean through an empty-shell architecture.

## Empty Shell Architecture

The core SakugaVault repository contains no copyrighted media and hosts no video files.

- The core app handles authentication, watch history, metadata, comments, and playback orchestration.
- Video playback is resolved dynamically through independent scrapers and open APIs that return HLS playlist links when a user presses play.
- The repository stays focused on application state, user experience, and service orchestration rather than content hosting.

## Frontend Experience

The frontend is a decoupled React SPA housed in `sakugavault-web/` with these implemented core flows:

- Login page for authentication and session entry.
- Catalog page with a hero banner for the top trending anime.
- Horizontal genre rails for the top catalog plus a continue-watching strip.
- Hidden sidebar navigation for downloads, profile, search, and logout.
- Watch page with a player container, metadata panel, comments, similar-anime recommendations, metadata sync, and queue-download actions.
- Search page backed by the catalog search endpoint.
- Downloads page backed by a persisted download queue.
- Profile page backed by a profile summary endpoint.

## Backend Architecture

The backend follows a strict thin-controller, fat-service pattern in ASP.NET Core.

- Controllers handle routing, authorization, DTO validation, and HTTP responses.
- Services contain business logic, data shaping, metadata aggregation, and scraper orchestration.
- MySQL and EF Core are intended for relational state such as users, comments, watch history, downloads, and shadow title mappings.

## Scraper-Backed Content Path

SakugaVault now uses a small Node scraper service for catalog, search, metadata, and playback resolution. The service exposes the Consumet-compatible routes the backend expects while running `@consumet/extensions` directly.

- Home catalog and search results are loaded from live provider feeds through the scraper service.
- The backend tries the configured providers in order and falls back to the next provider when one fails.
- MySQL is still used for application state and stable local ids, but not as the primary catalog source.
- A shadow anime row may be created locally so comments, downloads, and watch history can remain relational and stable.

## Video Delivery Strategy

SakugaVault is designed around HLS playback and explicitly avoids storing video blobs in MySQL.

- Playback is resolved as chunked `.m3u8` streams rather than monolithic media files.
- Self-hosted deployments can point to object storage or local storage optimized for byte-range requests.
- The database should only persist lightweight metadata such as external stream references, not the media itself.

## Deployment

- The backend and MySQL stack are intended to run through Docker.
- The scraper service is expected to be run beside the backend locally, in Docker Compose, or as a separately hosted Node service.
- Community deployments should be able to start with a simple compose workflow.
- Early deployments should lean on free tiers, student credits, and low-cost self-hosted infrastructure.

## Local Development

- Create an untracked `.env` file locally or export the required environment variables in your shell.
- Required secret-backed values: `JWT_SIGNING_KEY`, `MYSQL_PASSWORD`, `MYSQL_ROOT_PASSWORD`.
  - In Docker Compose, `JWT_SIGNING_KEY` is forwarded into the API container as `ASPNETCORE_JWT_SIGNINGKEY`.
- Required database values for compose: `MYSQL_DATABASE`, `MYSQL_USER`, and optionally `MYSQL_HOST_PORT`.
- Optional scraper endpoint override for compose/API containers: `SCRAPER_BASE_URL`.
- Optional scraper host port override: `SCRAPER_HOST_PORT`.
- Runtime auth model: short-lived JWT access tokens plus an `HttpOnly` refresh-token cookie. The refresh cookie is what keeps users signed in across page reloads.
- `docker-compose up --build` starts MySQL, the API, and the React frontend locally.
- The API container reads `SCRAPER_BASE_URL` and calls the local `scraper` compose service by default.
- The old `riimuru/consumet-api` container is still available through the `legacy-consumet` compose profile, but it is no longer used by default.
- The scraper can also be run outside Docker from the repository root with `npm run scraper:start`.
- The React client can also be run outside Docker from `sakugavault-web/` when you want a normal local Vite workflow.
- If you run the API outside Docker, keep the scraper service available at `http://localhost:3100` or override `Scrapers__ConsumetBaseUrl`.
- Frontend startup:
  - `cd sakugavault-web`
  - `npm install`
  - `npm run dev`
- The Vite client serves on `http://localhost:5173` and is already aligned with the backend CORS configuration.
- If you want the browser to call a non-default API origin directly, set `VITE_API_BASE_URL` in your local shell before starting the frontend.
- In Development, the API applies EF Core migrations on startup. Seed data only runs if `Catalog:EnableDevelopmentSeedData` is explicitly enabled.
- Swagger is available at `http://localhost:8080/swagger` when the API is running in Development.

## Study Docs

- API reference: `docs/API_REFERENCE.md`
- Backend file guide: `docs/BACKEND_FILE_GUIDE.md`
- Frontend file guide: `docs/FRONTEND_FILE_GUIDE.md`
- Migrations guide: `MIGRATIONS.md`
