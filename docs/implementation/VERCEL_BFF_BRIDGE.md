# Vercel to VPS transport preparation

Status: Next transport and standalone VPS gateway implemented; disabled by default. This does not enable public
accounts or close RELEASE_GATES. VPS installation and external-origin acceptance
are still required. No deployed backend, database, runtime or provider was changed.

## Intended request path

Browser -> same-origin Next `/bff/*` route handler on Vercel -> authenticated
HTTPS gateway on the VPS -> loopback ASP.NET BFF -> loopback Identity/PostgreSQL.

The bridge does not connect to PostgreSQL, call Identity directly, mint sessions,
or implement authentication. The ASP.NET BFF remains responsible for antiforgery,
session validation, account policy and error precedence after transport checks.

## Delivered boundary

- Exact allowlist of existing B1/B2/B3 account routes and methods. No arbitrary
  target, query forwarding, diagnostic/operator routes or locale rewriting.
- Fixed HTTPS browser and gateway origins from server configuration. Additional
  aliases fail closed; choose a stable browser URL before enabling accounts.
- Separate gateway key, injected only server-side. Browser Authorization,
  forwarded/subject/client headers and unknown cookies never reach the gateway.
- 16 KiB request limit before media-type/origin checks, 1 MiB response bound,
  45 second abort budget across fetch and response body, no cache, no redirects
  and exactly one application-level dispatch. A 503 is never a NotProcessed proof.
- Only the four existing auth cookie names pass. Duplicates in browser requests
  are discarded. Response cookies require Secure, HttpOnly, Path=/, SameSite=Lax
  and no Domain; separate Set-Cookie fields and deletion attributes are retained.
- Upstream responses must be JSON (or 204). No upstream exception text,
  diagnostic/CORS headers or arbitrary cookies are returned. Retry-After is
  preserved; longer reset cooldowns are not clamped by this transport.

## Configuration (server-only)

| Variable | Meaning |
| --- | --- |
| `SMARTCORE_BFF_BRIDGE_ENABLED` | `false` by default; only literal `true` opts in |
| `SMARTCORE_BROWSER_ORIGIN` | Stable exact HTTPS frontend origin, no path/query |
| `SMARTCORE_BFF_GATEWAY_ORIGIN` | Separate fixed HTTPS VPS gateway root |
| `SMARTCORE_BFF_GATEWAY_KEY` | Separate 43–128 character base64url gateway credential |

Never prefix these with NEXT_PUBLIC. Provision the gateway key in secret settings
on Vercel and the VPS, independently of Identity client, receipt and session keys.
Missing/invalid enabled configuration returns code-only 503. Disabled transport
retains JSON 404 and all existing account UI flags remain false by default.

## Required next slice, before activation

1. Obtain the Debian host's public address, current listeners and selected free or
   purchased backend DNS name. Preserve services and occupied ports.
2. Install the standalone VPS gateway after staging acceptance. Its implementation
   is `deploy/gateway/server.mjs`; it validates the dedicated key in constant time
   before dispatch, accepts only account paths and rebuilds forwarded headers.
   See `VPS_GATEWAY.md`. TLS verification stays enabled. The gateway key alone is
   not a browser session or CSRF proof.
3. Add and test an explicit restricted staging deployment policy in ASP.NET.
   Current HostConfiguration rejects non-loopback browser origins and Production;
   changing a Vercel environment variable cannot bypass that requirement.
4. Run real Next -> TLS gateway -> two BFF hosts -> PostgreSQL login/refresh/logout
   and recovery acceptance with disposable test accounts, including forged gateway
   headers, untrusted origins, rejected keys, response loss and persistent key-ring
   continuity. Current transport tests are not this end-to-end evidence.
5. Complete applicable release gates before allowing real users. Kavenegar stays
   postponed. No real OTP provider or production account flag is enabled here.

## Verification commands

From `web/identity`:

```sh
npm run test:bridge
npm run check
npm run typecheck
npm run build
npm run smoke
npm run smoke:bridge
```

Unit transport tests use synthetic fetch peers; enabled production Next smoke
checks only locally rejected requests and never reaches a live gateway. Existing
production shell smoke checks disabled routing, fa/en, CSP nonces and local TLS.
The Browser CI workflow runs both transport suites alongside existing checks.

## Operator inputs (no secrets)

The owner should run `deploy/preflight/identity-vps-readonly.sh` on Debian and
provide its output plus the host's public IP/DNS and chosen backend hostname.
Do not paste .env, passwords, connection strings, PFX files or provider keys.
