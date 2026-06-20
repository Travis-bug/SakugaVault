#!/bin/sh
# Lock the origin so only Cloudflare can reach HTTP/HTTPS. Anyone who finds the
# VM's real IP and tries to hit it directly — bypassing Cloudflare's DDoS/WAF —
# is dropped. SSH stays open only to your admin IP. Everything else (MySQL,
# Redis, scrapers) is already internal-only in docker-compose.prod.yml.
#
# Run ON THE VM as root, AFTER confirming you can SSH in:
#   ADMIN_IP=<your.current.ip> sh ops/firewall-cloudflare.sh
#
# Find your current IP first (from your laptop):  curl -4 ifconfig.co
# Re-run anytime Cloudflare changes its ranges (rare). Needs ufw + curl.
#
# NOTE: TLS here uses Caddy's Cloudflare DNS-01 challenge (outbound API call),
# so locking inbound 80/443 to Cloudflare does NOT break certificate issuance.
set -eu

ADMIN_IP="${ADMIN_IP:-}"

if ! command -v ufw >/dev/null 2>&1; then
	echo "Installing ufw..."
	apt-get update -y && apt-get install -y ufw
fi

# Reset cleanly, but keep SSH allowed before enabling so you don't lock yourself out.
ufw --force reset
ufw default deny incoming
ufw default allow outgoing

# SSH (22): restrict to your admin IP if provided (strongly recommended).
if [ -n "$ADMIN_IP" ]; then
	ufw allow from "$ADMIN_IP" to any port 22 proto tcp
	echo "SSH restricted to ${ADMIN_IP}"
else
	echo "WARNING: ADMIN_IP not set — leaving SSH (22) open to the world."
	ufw allow 22/tcp
fi

# HTTP/HTTPS (80,443): only from Cloudflare's published ranges.
echo "Allowing 80,443 from Cloudflare ranges..."
for ip in $(curl -fsSL https://www.cloudflare.com/ips-v4); do
	ufw allow from "$ip" to any port 80,443 proto tcp
done
for ip in $(curl -fsSL https://www.cloudflare.com/ips-v6); do
	ufw allow from "$ip" to any port 80,443 proto tcp
done

ufw --force enable
echo "---"
ufw status verbose
