# VPS transport status, 2026-10-18

Operator-reported evidence from the Debian VPS (not an independent full-stack test):

- Public IPv4: `87.107.54.244`; SSH remains on 22022.
- The local resolver returned `10.10.34.36` for DuckDNS and the proposed sslip.io
  name. DuckDNS API requests timed out. Do not retry its token-bearing plugin
  until connectivity and diagnostic redaction are addressed.
- The DuckDNS credential appeared in an upstream plugin exception. The operator
  confirmed rotating it. Do not reuse or publish the previous credential.
- Let's Encrypt IP certificate issuance succeeded with Certbot Snap 5.8.0,
  the webroot authenticator and the shortlived profile. The lineage is
  `/etc/letsencrypt/live/smartcore-identity-ip/`.
- A dedicated Nginx HTTP vhost for the IP preserves the challenge webroot at
  `/var/www/smartcore-identity-acme`. It must remain available for renewals.
- A separate Nginx TLS vhost listens on IPv4 port 8443. Its `/healthz` returns
  only `smartcore-tls-ready`; all other paths currently return 404. Request
  logging is disabled. Existing domain vhosts on 443 were not edited.
- The operator observed the expected response from their browser and from a
  verified local curl request. The configured Snap renewal timer was listed.
- `certbot renew --cert-name smartcore-identity-ip --dry-run
  --run-deploy-hooks --no-directory-hooks` completed successfully, including the
  configured `nginx -t && systemctl reload nginx` hook. Nginx's normal successful
  config-check diagnostics appeared on stderr.

The intended server-only gateway origin is now `https://87.107.54.244:8443`.
It is **not yet connected to the BFF**. The VPS env still has bridge activation
disabled; do not enable it or the public account flags from this evidence alone.
No private database, session keys or Data Protection wrapping keys were changed.

## Next checkpoint

Run `deploy/preflight/identity-bridge-readonly.py` as the existing application
user, without sudo. It reads only bundle build metadata and file ownership/modes,
invokes version/status checks, lists relevant listener addresses and checks the
fixed TLS probe. It never reads `.env`, private configuration.json, key-ring
contents, command-line process arguments, logs or systemd environment settings.

Use its bundle source/runtime and listener inventory to prepare an upgrade which
preserves existing private state. The ASP.NET external-origin policy, restricted
staging runner/service, gateway installation and real two-host account acceptance
are still outstanding. Production remains blocked by the existing release gates.

The explicit Vercel project lookup still returned scope authorization 403 for
`team_KnO2S7RhxYmHmHRdrxZkmwwI` / `prj_94gXH384Z8gZHG3kpmYZgbhXYb8I`.
No authenticated local Vercel CLI was available as a fallback. Do not infer that
reconnecting again will resolve this or that server variables were provisioned.
