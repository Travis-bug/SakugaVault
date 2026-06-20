# SakugaVault — Production Deploy (Oracle Cloud Always Free, ARM)

Launch-day checklist. Run top to bottom. Assumes:

- Domain `sakugavault.com` is already on **Cloudflare** (proxied) — done.
- An **Oracle Cloud** account — done.
- This repo is reachable from the server (GitHub, or copied up — see Step 3).

The whole stack is ARM-ready (every image has an `arm64` build), so Oracle's
free Ampere A1 runs it natively. The API **auto-applies its EF migrations on
startup**, so the database schema is created on first boot — no manual step.

---

## 1. Create the Oracle ARM instance

OCI Console → **Compute → Instances → Create instance**:

- **Image:** Canonical **Ubuntu 24.04** (or 22.04).
- **Shape:** Change shape → **Ampere** → **VM.Standard.A1.Flex** →
  **4 OCPUs / 24 GB RAM** (the full Always Free allowance). If a region says
  "out of capacity," try another availability domain or region.
- **Networking:** create/keep a VCN with a **public subnet**; **assign a public
  IPv4**.
- **SSH keys:** upload your public key (or let it generate one and download the
  private key). You'll SSH in as user `ubuntu`.

Note the instance's **public IP** — you'll need it for Cloudflare (Step 7).

> Always-Free ARM instances can be reclaimed only if *idle*; an always-on web
> server is never idle, so you're fine.

## 2. Open ports at the Oracle level

Oracle has a **cloud firewall** (Security List) separate from the host. In the
VCN → your subnet → **Security List** → add **Ingress Rules**:

| Source | Protocol | Dest Port | Why |
|--------|----------|-----------|-----|
| `<your laptop IP>/32` | TCP | 22 | SSH (find it: `curl -4 ifconfig.co`) |
| `0.0.0.0/0` | TCP | 80 | HTTP (host firewall will narrow this to Cloudflare in Step 9) |
| `0.0.0.0/0` | TCP | 443 | HTTPS (same) |

(Leaving 80/443 open at the OCI level is fine — Step 9 locks them to Cloudflare
on the host itself.)

## 3. SSH in and install Docker

```bash
ssh ubuntu@<PUBLIC_IP>

# Docker Engine + Compose plugin
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ubuntu
newgrp docker   # apply the group now without re-login

docker --version && docker compose version   # sanity check
```

## 4. Get the code onto the server

```bash
# Private repo via a GitHub token (Settings → Developer settings → tokens):
git clone https://<GITHUB_TOKEN>@github.com/<you>/SakugaVault.git
cd SakugaVault
```

Alternative (no git): from your laptop, `scp` the folder up — but exclude the
heavy local dirs:

```bash
# run on your laptop, in the project parent dir
rsync -az --exclude '.git' --exclude 'node_modules' --exclude 'bin' \
  --exclude 'obj' --exclude 'backups' \
  "SakugaVault/" ubuntu@<PUBLIC_IP>:~/SakugaVault/
```

## 5. Create `.env.prod` (secrets — server only, never committed)

```bash
cp .env.prod.example .env.prod
nano .env.prod
```

Fill in:

- `DOMAIN=sakugavault.com`
- `ACME_EMAIL=<your email>`
- `CLOUDFLARE_API_TOKEN=` → Cloudflare → My Profile → API Tokens → **Create
  Token** → template **"Edit zone DNS"** → Zone = `sakugavault.com`. Paste it.
- `MYSQL_PASSWORD` / `MYSQL_ROOT_PASSWORD` → strong, different. Generate:
  `openssl rand -base64 24`
- `JWT_SIGNING_KEY` → `openssl rand -base64 48`
- Leave the paid scrapers (`CRAWLEE_*`, `SCRAPY_*`) disabled unless you have
  credits; `ANIMEPAHE_RESOLVER_ENABLED=true` is the free English path.

## 6. First launch

```bash
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build
```

First build takes a while (it compiles the .NET API and the web bundle on the
box). Watch it come up:

```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs -f api      # Ctrl-C to stop
```

You want `mysql (healthy)` and the api log line `Now listening on...`. The API
creates the DB schema automatically on this first run.

> Production does **not** load the dev seed catalog, so the site starts with an
> empty anime list. Populate it via your normal metadata-sync flow after launch.

## 7. Point Cloudflare DNS at the VM

Cloudflare → **DNS → Records**. Edit the placeholder records to your VM IP:

| Type | Name | Content | Proxy |
|------|------|---------|-------|
| A | `sakugavault.com` (`@`) | `<PUBLIC_IP>` | **Proxied** 🟠 |
| CNAME | `www` | `sakugavault.com` | **Proxied** 🟠 |

(Leave the MX + TXT email records as **DNS only**.)

## 8. Set Cloudflare SSL/TLS to Full (strict)

Cloudflare → **SSL/TLS → Overview → Full (strict)**. This is the MITM-safe,
end-to-end-encrypted mode. Caddy will fetch a Let's Encrypt cert via the
Cloudflare DNS challenge automatically (works even with the proxy on) — give it
1–2 minutes after the stack is up.

## 9. Lock the host firewall to Cloudflare only

So nobody can bypass Cloudflare by hitting the raw IP. Oracle's Ubuntu image
ships restrictive iptables rules; hand control to `ufw` first, then run the
script:

```bash
# Hand firewall management to ufw (removes Oracle's persisted iptables rules)
sudo apt-get purge -y netfilter-persistent iptables-persistent || true

# Allow 80/443 only from Cloudflare; SSH only from your IP.
ADMIN_IP=$(curl -4 -s ifconfig.co)   # or set your fixed IP explicitly
sudo ADMIN_IP="$ADMIN_IP" sh ops/firewall-cloudflare.sh
```

> If your home IP is dynamic, set `ADMIN_IP` to your current one and re-run when
> it changes. Locked out? Use the OCI Console → instance → **Cloud Shell /
> serial console** to fix `ufw`.

## 10. Verify

```bash
curl -I https://sakugavault.com            # expect HTTP/2 200
curl -s https://sakugavault.com/health     # API health via the proxy
```

Then open **https://sakugavault.com** in a browser — valid padlock, site loads.
That's launch. ✅

---

## Day-2 operations

**Logs**
```bash
docker compose -f docker-compose.prod.yml logs -f <service>   # api, web, animepahe...
```

**Restart / stop**
```bash
docker compose -f docker-compose.prod.yml restart api
docker compose -f docker-compose.prod.yml down                # stop all (data volumes persist)
```

**Deploy an update**
```bash
git pull
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build
```
Only changed images rebuild; DB migrations apply automatically on api restart.

**Backups** — the `db-backup` sidecar dumps to `./backups/` on the schedule in
`.env.prod` (`BACKUP_INTERVAL_SECONDS`, default daily; keeps `BACKUP_KEEP`). For
offsite copies, set `OFFSITE_UPLOAD_CMD` (e.g. an `azcopy`/`rclone` one-liner).

**Restore from a dump**
```bash
gunzip -c backups/<dump>.sql.gz | \
  docker compose -f docker-compose.prod.yml exec -T mysql \
  mysql -u"$MYSQL_USER" -p"$MYSQL_PASSWORD" "$MYSQL_DATABASE"
```

**Scale up** — if the box gets busy: OCI Console → instance → **Edit shape** →
raise OCPU/RAM (or move to a paid shape). No re-deploy needed; reboot and the
stack restarts via `restart: unless-stopped`.

---

## When to graduate off the single VM
1. **Managed DB first** (data is irreplaceable) — when you can't tolerate the
   backup window or need HA.
2. **Frontend → Vercel/CDN** — when global first-paint latency matters.
3. **Backend → Azure Container Apps / ECS** — when one box is consistently
   >70% CPU/RAM or you need zero-downtime deploys + autoscaling.
The compose files port over cleanly, so starting here doesn't lock you in.
