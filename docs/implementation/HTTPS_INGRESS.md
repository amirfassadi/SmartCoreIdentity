# B1 development HTTPS ingress

Task 4 delivery. Development/Testing only; no VPS/production deployment or completed
B1 account/browser acceptance. The opaque-cookie principal and session-bound CSRF
pipeline are the next task. Login remains unavailable until those guards and the
full account browser CI acceptance are complete.

## Runtime boundary

[https-ingress.mjs](../../deploy/development/https-ingress.mjs) uses the pinned Node
24 runtime and built-in HTTPS/HTTP modules. It binds loopback only and requires a
fixed HTTPS external origin. TLS key material is provided from a local secret mount;
the process never generates/trusts a public certificate or modifies the machine CA
store. TLS material is separate from the Data Protection wrapping certificate.

Exact `/bff` and `/bff/` prefixes go only to two private .NET hosts, round-robin per
request. No locale rewrite, automatic retry, redirect following or same-request
failover. Upstream redirects must resolve within the fixed external HTTPS origin. `/auth`, `/dev` and `/operator` roots/prefixes are blocked, not proxied to
Identity or Next. Ambiguous encoded slash/backslash/dot-segment targets are rejected.
Upstream calls have a 45-second absolute/inactivity budget, larger than current BFF
owner/wait budgets. An ambiguous transport failure returns a code-only unavailable
response; this is **not** a NotProcessed proof and never authorizes command replay.

Request cookies have explicit independent allowlists: only validated fa/en
`smartcore-locale` reaches Next; only opaque session/antiforgery cookies reach .NET.
Duplicate/invalid cookies are discarded and the original Cookie header is never
forwarded. Response cookies also have separate allowlists. Next cannot set an auth
cookie; allowed cookies must retain their Secure/Lax/Path=/ attributes and no Domain;
BFF cookies additionally require HttpOnly. Multiple Set-Cookie fields stay separate.

Public trusted-client, subject, diagnostic, bearer, nonce and forwarded headers do
not pass the request whitelist. Forwarded host/proto/client IP are rebuilt from the
fixed origin and trusted connection. The .NET host requires `Bff:ExternalOrigin`
and accepts one symmetric forwarded hop only from exact loopback addresses; allowed
forwarded hosts are restricted to its configured origin host. This does not replace
the exact Origin and Fetch-Site checks in the upcoming guard.

BFF bodies are bounded at 16384 bytes before dispatch; oversize returns JSON 413.
The host guard still owns content-type, Origin/Fetch-Site, CSRF and JSON/DTO precedence.
No request body, token, cookie or upstream exception message is logged.

## Run locally

Provide an untracked TLS key/certificate with localhost SAN and configure the test
runner/client to trust that certificate explicitly. Never disable TLS verification.
Both .NET hosts must share their separate session keys, wrapping certificate and
Data Protection directory, and use the same configured external origin.

```sh
SMARTCORE_INGRESS_ENVIRONMENT=Development \
SMARTCORE_INGRESS_ORIGIN=https://localhost:7443 \
SMARTCORE_TLS_KEY=/absolute/secret-mount/localhost.key \
SMARTCORE_TLS_CERT=/absolute/secret-mount/localhost.crt \
SMARTCORE_NEXT_ORIGIN=http://127.0.0.1:3181 \
SMARTCORE_BFF_ORIGINS=http://127.0.0.1:5171,http://127.0.0.1:5172 \
node deploy/development/https-ingress.mjs
```

`Bff__ExternalOrigin=https://localhost:7443` is required on each .NET host.

## Evidence boundaries

- [TLS boundary test](../../tests/ingress/https-ingress.test.mjs): verifies certificate
  trust, request/response cookie and header separation, two-peer routing, B2/B3 JSON
  rejection, pre-dispatch size rejection and exactly one call after socket failure.
  Its backends are controlled HTTP peers, not ASP.NET or Next.
- Production Next smoke: actual Next pages through this HTTPS ingress; fa/en direction,
  nonce preservation and cookie response policy. BFF peers in that job are controlled.
- [Two-host smoke](../../scripts/bff-host-smoke.py): actual two ASP.NET processes with
  PostgreSQL 17 through this TLS ingress; B2/B3 JSON 404, exact route 405/Allow and
  no-store. Next is not called in this job.
- CI must pass these checks before runtime evidence is claimed. No browser hydration,
  real session/CSRF, or registration/login/refresh/logout/reset E2E is certified here.

The four adjacent competing-error requests in tests/browser/b1-guard-precedence.json
must be the first live guard tests in task 5, with zero upstream calls asserted.
`personId` remains prohibited in browser DTOs. B2 will re-review OTP/register retry
windows; B3 will separately review reset cooldowns rather than clamp them to B1.

## Verified code revision

Revision `d6ea1da22cb6bd5d1d9c724ef2b8d649b7935d89` passed
[Browser B1 infrastructure CI 38223166330](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38223166330).
Both job logs were inspected. Job 114724111216 built the .NET host with zero
warnings/errors and passed TLS requests to two real hosts on PostgreSQL 17.
Job 114724111533 passed production Next-through-TLS checks and the independent
TLS cookie/header/no-retry boundary checks. Integration revision
`09834bce323f42f84b256e8875a5f142bd4d4480` has the identical code tree and passed
infrastructure run 38223170805. Neither job certifies live CSRF/session guards or
browser account E2E; those remain the next acceptance work.
