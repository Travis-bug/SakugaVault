# Caddy with the Cloudflare DNS plugin baked in. The stock caddy image can only
# solve ACME challenges over HTTP/TLS-ALPN, which fails when Cloudflare's proxy
# (orange cloud) terminates TLS at the edge. The DNS-01 challenge validates by
# writing a TXT record via the Cloudflare API instead, so certificates issue and
# auto-renew while proxied.
FROM caddy:2-builder-alpine AS builder
RUN xcaddy build --with github.com/caddy-dns/cloudflare

FROM caddy:2-alpine
COPY --from=builder /usr/bin/caddy /usr/bin/caddy
