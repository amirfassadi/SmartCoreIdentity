# B1 account handlers and browser acceptance

Development/Testing implementation, 2026-10-12. Production startup and public bind
remain rejected. Account handlers require explicit `Bff__AccountsEnabled=true`;
the browser form requires `NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED=true` at build time.
These switches enable isolated acceptance only and do not authorize deployment.

The reviewed browser contract still defines exactly CSRF, login, me, me/action and
logout. `tools/generate_bff_browser_client.py` reproduces the committed browser
TypeScript definitions and route allowlist; `--check` fails on contract drift.
The private adapter uses Identity's trusted DTOs and explicitly constructs the
browser profile without internal IDs/tokens. Requests have bounded deadlines,
no redirect, no cookie jar and no automatic replay. Login subjects use a keyed
canonical contact; session subjects match the BFF core's keyed session convention.
The Identity error envelope is recognized and mapped to browser machine codes;
upstream text/trace IDs never reach the browser. The existing fixed one-minute B1
admission limit maps to 60-second Retry-After without changing B3 cooldowns.

Login persists sealed tokens before issuing a fresh absolute-expiry __Host session
cookie and clearing prelogin antiforgery. Reads use the existing RefreshIfDue and
its cross-process journal/lock/circuit semantics. Only a recognized Identity JSON
401 invokes matching-session RejectAccess; configuration failures and outages
preserve cookies. A final local lookup denies profile delivery after local closure.
This does not claim to eliminate the bounded access-token race described in BFF_CORE.

`RequestLogout` commits local intent separately from upstream completion. The
browser acknowledges 204 and clears both cookies only after that commit succeeds.
The existing cookie-free maintenance service completes/retries upstream logout;
the existing core Logout API retains its prior request-plus-completion behavior.
Pre-intent failure never reports successful logout.

The fa/en client keeps CSRF only in memory and never retries credential commands.
Web Locks serialize account requests with their fresh CSRF bootstrap across tabs;
login is broadcast after fresh CSRF is obtained. The lock ends after response
headers/cookie effects; slow profile-body parsing remains outside so it cannot block
logout. This includes reads because an
anonymous/terminal 401 can clear cookies and must not arrive after a newer login.
The lock coordinates browser cookie responses, not Identity token rotation: the BFF
core remains authoritative and its HTTP concurrency suite proves cross-host single-flight.
Without Web Locks, the same ordered login notification applies; Chromium acceptance
exercises the supported shared-lock path.
Errors are localized by code; Retry-After disables new actions for the stated
bounded period. BroadcastChannel carries only transition types. The storage
fallback contains type and non-secret timestamp, never proof/profile material.
Tab transitions invalidate CSRF, clear private state, abort obsolete reads and
advance a UI generation. Login receivers fetch current state; focus/visibility/
pageshow recheck through background reads, never a foreground polling assertion.
The production default build continues to display the disabled account state.

## Acceptance boundaries

The original zero-Identity-dispatch guard suite is still run with handlers disabled.
The separate account runner enables handlers against a real private Identity host,
a disposable PostgreSQL 17 database, two BFF hosts and real HTTPS ingress. Its
Ready account is created via private registration and protected inbox fixtures,
never a public seed route. A private CLI fixture marks stored access due and reads
non-secret generation/state counters; it exposes no diagnostic HTTP endpoint.

The HTTP suite checks actual login/profile DTOs and cookies, wrong credentials,
prelogin/session CSRF rebinding, same-owner second login, exactly one rotation for
four parallel reads, Identity access rejection, pre-intent lock-timeout/cookie preservation, outage preservation, durable local
logout, bound intent replay and cookie-free maintenance recovery. Winter/summer
2026 Asia/Tehran checks execute in Node, .NET and Chromium. Responses use the exact browser
OpenAPI validator. The fault database is isolated; no VPS database is modified.

The Chromium suite uses production Next and the same actual TLS/BFF/Identity stack.
Only the disposable certificate's SPKI is trusted (not blanket TLS bypass). It
checks real login, HttpOnly cookies, public SSR isolation, production nonce CSP
hydration, fa/en direction, RTL mobile layout, explicit locale persistence,
BroadcastChannel and storage fallback, and discarded delayed profile responses.
Credentials are passed privately over stdin. Traces, screenshots, network dumps
and credential-bearing assertion output are suppressed.

Run:

```sh
python tools/generate_bff_browser_client.py --check
npm ci --ignore-scripts --prefix web/identity
NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED=true npm run build --prefix web/identity
npm ci --ignore-scripts --prefix web/identity/e2e
npx --prefix web/identity/e2e playwright install --with-deps chromium
# Supply ConnectionStrings__Bff for a disposable PostgreSQL 17 database only:
SMARTCORE_BROWSER_E2E=yes python scripts/bff-account-smoke.py
```

CI must pass before these controls are called verified. B2 registration/OTP UI and
B3 enrollment/reset/password-change UI remain open, as do public-user release gates,
provider selection, canonical Platform provenance and operator deployment inputs.
B1 does not certify the complete registration/reset browser journey or production.
