#!/usr/bin/env bash
# One-shot installer: restrict the Docker-published web ports (80/443) to
# Cloudflare's IP ranges using the DOCKER-USER chain (the only chain that
# actually filters traffic to containers — ufw's INPUT rules are bypassed by
# Docker's nat/FORWARD rules). SSH (22) is never touched, so this cannot lock
# you out. Idempotent and boot-persistent via a systemd oneshot after Docker.
set -euo pipefail

# --- 0. Tear down the failed ufw experiment + any leftover deadman timers ---
pkill -f "sleep 180" 2>/dev/null || true
pkill -f "force disable" 2>/dev/null || true
ufw --force disable 2>/dev/null || true

# --- 1. Drop the rule-applier the systemd unit will run on every boot ---
install -d /usr/local/sbin
cat > /usr/local/sbin/cf-origin-firewall.sh <<'APPLY'
#!/usr/bin/env bash
# Allow only Cloudflare (+ established) to reach container ports 80/443 on the
# public interface; drop everyone else. Re-runnable: flushes our managed rules
# in DOCKER-USER and rebuilds them from the live Cloudflare IP list.
set -euo pipefail
IFACE="$(ip -o route get 1.1.1.1 | sed -n 's/.*dev \([^ ]*\).*/\1/p')"

apply() {  # $1=iptables|ip6tables  $2=cloudflare-ips-url
  local ipt="$1" url="$2" cidr
  "$ipt" -F DOCKER-USER 2>/dev/null || true            # clear our prior rules
  "$ipt" -A DOCKER-USER -i "$IFACE" -m conntrack --ctstate ESTABLISHED,RELATED -j RETURN
  for cidr in $(curl -fsSL "$url"); do
    "$ipt" -A DOCKER-USER -i "$IFACE" -s "$cidr" -p tcp -m multiport --dports 80,443 -j RETURN
  done
  "$ipt" -A DOCKER-USER -i "$IFACE" -p tcp -m multiport --dports 80,443 -j DROP
  "$ipt" -A DOCKER-USER -j RETURN                       # Docker's default tail
}

apply iptables  https://www.cloudflare.com/ips-v4
apply ip6tables https://www.cloudflare.com/ips-v6
echo "cf-origin-firewall applied on $IFACE"
APPLY
chmod +x /usr/local/sbin/cf-origin-firewall.sh

# --- 2. systemd unit: reapply after Docker is up (survives reboot/daemon restart) ---
cat > /etc/systemd/system/cf-origin-firewall.service <<'UNIT'
[Unit]
Description=Restrict container web ports to Cloudflare (DOCKER-USER)
After=docker.service
Requires=docker.service

[Service]
Type=oneshot
ExecStart=/usr/local/sbin/cf-origin-firewall.sh
RemainAfterExit=yes

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable --now cf-origin-firewall.service >/dev/null 2>&1

echo "INSTALL_DONE v4=$(iptables -S DOCKER-USER | wc -l) v6=$(ip6tables -S DOCKER-USER | wc -l) ufw=$(ufw status | head -1 | awk '{print $2}')"
