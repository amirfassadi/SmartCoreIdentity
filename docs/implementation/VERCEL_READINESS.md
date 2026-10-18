# Vercel frontend and VPS backend: readiness

Status: frontend preview configuration ready for validation. Live Vercel-to-VPS
account traffic is blocked by the current backend deployment gates, not enabled.
The current BFF accepts Development/Testing with a fixed HTTPS **loopback** origin
only. Changing a frontend URL or allowing CORS does not satisfy those gates.

## Owner preparation

- Connect Vercel to only `amirfassadi/SmartCoreIdentity`.
- Import the working branch `review/identity-bff` (the default branch may differ).
- Root Directory: `web/identity`; framework: Next.js; Node: 24.x.
- Keep all three account flags in `.env.example` false for a UI preview.
- Use Vercel deployment protection for previews. Check the preview is protected
  before sharing a link. Do not configure real-user login on a preview URL.
- Choose the permanent frontend domain and the backend TLS hostname. Supply
  domain names, not registrar passwords. A vercel.app address can host the preview.
- Select an email sender/domain and its verification process. Enter provider
  credentials through the eventual server secret configuration, not client env.
- Do not point a Vercel rewrite at `localhost:7443`: localhost there is Vercel,
  not the VPS. The private certificate is not a public origin certificate.

## Proposed live topology (not implemented)

Browser -> frontend domain on Vercel -> /bff/* rewrite -> authenticated HTTPS
origin on VPS -> loopback BFF -> loopback Identity -> PostgreSQL.

Before implementing a live rewrite:

1. Accept a non-loopback BFF external-origin deployment policy explicitly; retain
   exact Origin, Host, Fetch-Site, body-size, content-type and CSRF validation.
2. Terminate public TLS on the existing VPS web server without replacing existing
   web/DNS/VPN services. Keep API, database, health and development inbox private.
3. Authenticate proxy-to-origin traffic. Strip client-supplied forwarding headers,
   validate the configured public host, and trust only the local reverse proxy.
4. Apply no-store to every BFF response, errors and Set-Cookie included. Never
   enable CDN caching on account routes. Preserve all HttpOnly/Secure/__Host-
   cookie properties. Keep browser calls same-origin and credentials server-side.
5. Test the real deployed chain: bootstrap/CSRF, registration, login, logout,
   recovery, refresh, rate limits, unknown paths, forwarding spoofing, cross-tab
   logout and session persistence. Preview domains must not expand origin trust.
6. Add real delivery before admitting external users; private inbox is testing only.

Official references:
https://vercel.com/docs/frameworks/full-stack/nextjs
https://vercel.com/docs/routing/rewrites

## Alternative: same-VPS deployment

Keep Next, BFF, Identity and PostgreSQL on this VPS. This removes the additional
Vercel-to-origin proxy hop, reduces configuration, and permits one-origin routing.
It still requires public-deployment acceptance, real TLS and delivery before public
registration. The opt-in systemd installer below improves **private** operation;
it does not change these release gates or expose any new port.

## Private service (new bundle only)

Stop the existing foreground runner first, then as the same normal application user:

```sh
cd /home/amir/smartcore-identity-private
sudo -v
python3 service.py install
sudo systemctl status smartcore-identity-private --no-pager
```

The installer verifies bundle hashes, database identity and free application ports.
It refuses an existing service file and never provisions a new database or keys.
The service runs as the current non-root user, keeps secrets in the existing
private directory and uses a separate temporary directory. KillMode=control-group
cleans up children even if Python dies; restart has a bounded rate limit.

```sh
sudo systemctl stop smartcore-identity-private
sudo systemctl start smartcore-identity-private
sudo journalctl -u smartcore-identity-private -n 40 --no-pager
```

Keep logs local if an unrelated system error prints sensitive context. Update a
service installation only while stopped; retain the existing dev-secrets directory.
No systemd service has been installed on the owner's VPS by this code change.
