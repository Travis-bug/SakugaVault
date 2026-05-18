Local Consumet Source Placeholder

This folder is intentionally empty in the tracked repo.

Why:
- The official `consumet/api.consumet.org` repository is currently unavailable through GitHub due to a DMCA takedown notice.
- SakugaVault therefore does not auto-fetch or vendor that source during Docker builds.

How to use this folder:
- If you already have a lawful local copy of the Consumet API source, place it in this folder so `docker compose --profile consumet-local-source up --build` can build it locally.
- Otherwise, leave this folder alone and point `CONSUMET_BASE_URL` in the root `.env` file at a separately managed Consumet-compatible service you control.
