# SakugaVault

## Project Overview

SakugaVault is a free, open-source, and self-hostable anime streaming platform designed to bypass corporate paywalls and monopolies. The platform aims to provide a premium, ad-free viewing experience while keeping the core repository legally clean through an empty-shell architecture.

## Empty Shell Architecture

The core SakugaVault repository contains no copyrighted media and hosts no video files.

- The core app handles authentication, watch history, metadata, comments, and playback orchestration.
- Video playback is resolved dynamically through independent scrapers and open APIs that return HLS playlist links when a user presses play.
- The repository stays focused on application state, user experience, and service orchestration rather than content hosting.

## Frontend Experience

The frontend is a decoupled React SPA with these core flows:

- Login page for authentication and session entry.
- Catalog page with a hero banner for the top trending anime.
- Horizontal genre rails for the top catalog.
- Hidden sidebar navigation for downloads, profile, search, and logout.
- Watch page with a player container, metadata panel, comments, and similar-anime recommendations.

## Backend Architecture

The backend follows a strict thin-controller, fat-service pattern in ASP.NET Core.

- Controllers handle routing, authorization, DTO validation, and HTTP responses.
- Services contain business logic, data shaping, metadata aggregation, and scraper orchestration.
- MySQL and EF Core are intended for relational state such as users, comments, watch history, and metadata.

## Video Delivery Strategy

SakugaVault is designed around HLS playback and explicitly avoids storing video blobs in MySQL.

- Playback is resolved as chunked `.m3u8` streams rather than monolithic media files.
- Self-hosted deployments can point to object storage or local storage optimized for byte-range requests.
- The database should only persist lightweight metadata such as external stream references, not the media itself.

## Deployment

- The backend and MySQL stack are intended to run through Docker.
- Community deployments should be able to start with a simple compose workflow.
- Early deployments should lean on free tiers, student credits, and low-cost self-hosted infrastructure.

## Local Development

- Create an untracked `.env` file locally or export the required environment variables in your shell.
- Required secret-backed values: `JWT_SIGNING_KEY`, `MYSQL_PASSWORD`, `MYSQL_ROOT_PASSWORD`.
- Required database values for compose: `MYSQL_DATABASE`, `MYSQL_USER`, and optionally `MYSQL_HOST_PORT`.
- `docker-compose up --build` starts the API and MySQL locally.
- In Development, the API applies EF Core migrations on startup and then runs the development seeder.
- Swagger is available at `http://localhost:8080/swagger` when the API is running in Development.

## Study Docs

- API reference: `docs/API_REFERENCE.md`
- Backend file guide: `docs/BACKEND_FILE_GUIDE.md`
- Migrations guide: `MIGRATIONS.md`
