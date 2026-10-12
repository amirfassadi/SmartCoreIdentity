# Stage B — bilingual browser account skeleton

Status: agreed architecture direction and implementation slice specification.
Private B1 handlers, generated browser types and fa/en forms now exist. Fresh
HTTP/Chromium acceptance is tracked in BFF_BROWSER_ACCOUNTS.md; B2 is privately verified in BFF_BROWSER_REGISTRATION.md; the complete private B3/Stage B journey is verified in BFF_BROWSER_RECOVERY.md.
The original ordered design below remains the acceptance specification.
Framework: Next.js App Router, React, TypeScript, Tailwind CSS. Initial UI locales:
fa and en. Initial market remains Iran; adding a language does not authorize sending
SMS to another country. Scope: account UI and a dashboard shell. Business domains,
multitenant publishing, templates, page builder and booking stay in later stages.

## Runtime boundary

Public pages use SSR without session lookup. Account/dashboard pages render a public
localized shell, then client components call same-origin /bff. No Server Component,
Server Action, Next.js route handler, proxy hook or prefetch performs authenticated
BFF reads, refresh, login or session mutation. Next.js never receives Identity tokens,
BFF client keys or diagnostic keys and does not own an authentication database.

The existing BFF owns sealed tokens, row-lock single-flight, durable dispatch journal,
refresh and logout recovery. Its opaque cookie is stable across core refresh today;
parallel requests are already serialized by that core. The client-only account
boundary avoids SSR cookie-response plumbing; it does not claim that parallel calls
bypass the existing BFF lock. BFF alone creates/deletes the authentication cookie.
The reverse proxy reconstructs page-request Cookie from an allowlist containing
only smartcore-locale (validated fa/en). Every other cookie, including unknown future
cookies, is omitted before Next.js. Duplicate/invalid locale entries are discarded;
never forward the original Cookie header. BFF request cookies have their own explicit
allowlist: __Host-smartcore-session and __Host-smartcore-antiforgery. Next.js cannot
set authentication/antiforgery cookies; proxy response policy enforces this boundary.

## Planned folders

| Path | Responsibility |
| --- | --- |
| frontend/account-web/package.json and lockfile | Pinned supported Node/Next/React dependencies; reproducible installs |
| frontend/account-web/src/app/[locale]/layout.tsx | lang, dir, local fonts and translated public shell |
| frontend/account-web/src/app/[locale]/(public) | SSR pages; no session-dependent data |
| frontend/account-web/src/app/[locale]/(account) | Registration/login/reset/recovery/password-change client forms |
| frontend/account-web/src/app/[locale]/dashboard | Client dashboard shell and session state |
| frontend/account-web/src/i18n | Supported-locale registry, next-intl routing/request setup, format configuration |
| frontend/account-web/messages/fa.json and en.json | ICU messages, labels, errors and accessibility text |
| frontend/account-web/src/lib/bff | Generated browser-contract types/client, safe error-code mapping, bounded busy handling |
| frontend/account-web/public/fonts | Self-hosted licensed fonts (Vazirmatn proposed); no Google Fonts dependency |
| frontend/account-web/tests/e2e | Playwright flows and fa/en direction tests |
| src/SmartCore.Identity.Bff.Host | ASP.NET Core host for existing BFF core, cookie/CSRF/Origin routes, maintenance registration |
| contracts/bff-browser.openapi.yaml | Browser cookie/CSRF DTOs, code-only errors; must be written before generation |
| deploy/development | Local HTTPS reverse-proxy/compose configuration; loopback/private Identity and inbox |

These are proposed paths to create, not existing artifacts. Generate the trusted
server-side Identity adapter from registration/authentication OpenAPI separately;
never reuse that trusted adapter as the browser client. Before writing any host code,
commit contracts/bff-browser.openapi.yaml for B1, review it, then generate the browser
client. That same file validates actual host response bodies/statuses/headers in CI,
not just client generation. Unknown fields and missing required response fields fail.
B2/B3 extend the contract before their host code. Generated output is checked against
committed contracts; drift fails build. Compile/build both runtimes in CI.

## Proxy and cookie contract

One configured HTTPS origin. Exact /bff and /bff/ prefixes route only to .NET; all
page/_next/font paths route to Next.js. No locale rewriting of /bff. Unknown BFF
routes fail in .NET rather than falling through to a Next handler. /dev inbox,
operator and Identity /auth paths are never exposed through this origin.

The proxy overwrites forwarded host/proto/client headers from its trusted connection;
public supplied X-Bff-Client-Key, X-Bff-Subject and diagnostic headers are discarded.
.NET trusts only configured proxy addresses and compares Origin to a configured
external-origin allowlist, not an attacker-selected Host/forwarded header. No permissive
credentialed CORS. BFF response Set-Cookie survives proxying without Domain rewriting.

Session cookie: __Host-smartcore-session, Secure, HttpOnly, SameSite=Lax, Path=/,
no Domain; bounded absolute expiry from the server session. Fresh cookie on login,
clear on committed local logout or terminal rejection. All mutations, including
anonymous login/register/reset (login CSRF matters), require exact approved Origin
and ASP.NET antiforgery cookie/header validation before forwarding or side effects.
Reject missing/null/wrong Origin for these browser routes. If Sec-Fetch-Site is
present, additionally require same-origin or none; reject cross-site/same-site/unknown
values using ORIGIN_REJECTED. Absence is allowed only with the required Origin and
CSRF checks still passing; this optional defense never replaces those checks.
No credentials in URL.

### Explicit CSRF/session binding

Implement an ASP.NET Core authentication handler for the custom opaque session cookie.
Before antiforgery generation/validation it resolves the cookie MAC against the BFF
store and populates HttpContext.User only from a valid, unexpired server-side session.
Its authenticated identity uses authentication type BffOpaque and a unique
ClaimTypes.NameIdentifier equal to the BFF session row UUID, not Person ID, raw cookie
or Identity access token. This identity is stable during refresh but fresh for every
new login. Invalid/closed/expired cookies never create an authenticated principal;
LogoutRequested is denied ordinary access, with narrowly scoped identity handling for
idempotent logout recovery. Resolution performs no refresh or upstream HTTP. Host
principal lookup does not replace final core state checks when requests race logout.

ASP.NET antiforgery now binds its token to that authenticated per-session identity;
it cannot infer a custom opaque cookie on its own. Anonymous bootstrap tokens bind
only to their antiforgery cookie, not to an authenticated session. GET /bff/csrf
uses the already established principal, returns the request token with no-store, and
does not authenticate anonymous users or refresh. Do not log the request token.

Configure antiforgery cookie __Host-smartcore-antiforgery: Secure=Always, HttpOnly,
SameSite=Lax, Path=/, no Domain. Request token travels in X-CSRF-Token; the browser
never reads that HttpOnly cookie. On successful login (new BFF session) and committed
logout/terminal session loss, invalidate the previous antiforgery cookie. Frontend
clears its in-memory request token and fetches /bff/csrf again before the next POST.
Pre-authentication tokens must not be reused after login; previous-session tokens
must fail after logout/login even for the same Person. Fresh GET responses are tied
to the current session, never a caller-supplied ID. Startup must register authentication
before antiforgery, and both BFF hosts share a durable protected ASP.NET Data Protection
key ring and application name; otherwise cross-host CSRF tokens will fail. These keys
are separate from Identity/BFF MAC/material keys. On Linux persist the shared key
ring in an access-restricted durable volume and explicitly encrypt key XML with
ProtectKeysWithCertificate using a separate X.509 wrapping certificate. Both hosts
receive the same certificate/private key via read-only secret mounts, provisioned
outside images/git and outside the key-ring volume; non-root service-only permissions.
The encrypted XML volume alone must not contain its decryption key. Missing wrapping
certificate/key ring must fail startup rather than fall back to plaintext/ephemeral
keys. For rotation deploy new wrapping/decryption certificates to both hosts, retain
old decryption certificates for still-live keys/tokens, then switch new-key encryption;
back up encrypted ring and protected wrapping secrets separately and test restore.
CI asserts encrypted XML, cross-host validation and startup failure without wrapping
material; certificate provisioning/rotation is deployment work, not done evidence.

No-store on all account/BFF material; no shared cache, logs or tracing payload capture
for passwords, OTP, recovery code, cookies or CSRF tokens. A locale cookie is separate
and is not authentication. Antiforgery validation failures return a stable code
CSRF_REJECTED (403) with no upstream dispatch; Origin failures use ORIGIN_REJECTED.

## Security headers and rendering decision

For B1–B3 all HTML, including the small public/localized shells, is dynamically
rendered with a fresh cryptographic CSP nonce per response and Cache-Control:
no-store. No ISR/static HTML/shared HTML cache in this slice. This deliberately pays
the nonce rendering cost; future cacheable business sites need a separate reviewed
CSP/cache strategy before stage F. Hashed static JS/CSS/font assets may still be
cached; the HTML nonce/header pair must never be reused from a shared cache.

Next.js creates the nonce itself (never trusts public x-nonce/CSP request headers)
and applies the identical nonce to framework/custom scripts and CSP. The ingress
strips those public headers; the response header remains paired with the rendered
HTML. Production baseline: default-src 'self'; script-src 'self' 'nonce-{fresh}'
'strict-dynamic'; style-src 'self' 'nonce-{fresh}'; img-src 'self' data:; font-src
'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self';
frame-ancestors 'none'. Explicit style-src-attr 'none': B1 forbids React style props and HTML style attributes
in our code and imported B1 components. Use Tailwind/CSS classes, not attribute
nonces (which cannot authorize style attributes). No style-src-attr unsafe-inline
exception in B1; future calendar/menu libraries require a separate reviewed policy.
No script unsafe-inline/unsafe-eval in the production build.
Any required style/library exception is explicit, minimal and reviewed before merge;
B1 must demonstrate hydration works under this policy, not silently disable CSP.
Development HMR exceptions stay separate; browser acceptance builds Next in production
mode and tests CSP violations, not just presence of the header.

At the public HTTPS ingress apply Referrer-Policy: no-referrer,
Permissions-Policy: camera=(), microphone=(), geolocation=(), X-Content-Type-Options:
nosniff and X-Frame-Options: DENY (alongside CSP frame-ancestors). Production HSTS:
max-age=31536000 after TLS acceptance; no includeSubDomains/preload until the future
domain rollout is decided. Local development must not set sticky HSTS on user hosts;
CI can exercise the production-header policy on its isolated HTTPS test origin.
Test headers on pages, BFF responses and failure responses as applicable; avoid
conflicting duplicate CSP headers at Next/proxy layers.

## Proposed browser routes

| Route | Behavior and boundary |
| --- | --- |
| GET /bff/csrf | Antiforgery bootstrap, no auth/refresh and no-store |
| POST /bff/register plus verify/resend/setup/complete | Explicit mappings to existing registration contract; bounded idempotency headers, no generic proxy |
| POST /bff/login | Trusted server login; BffRefresh.Create; set opaque cookie, discard token DTO from browser response |
| POST /bff/me | Background session read; host may RefreshIfDue(Background), then private Identity /me; safe profile DTO only |
| POST /bff/me/action | Explicit user-navigation/continue read; host may RefreshIfDue(UserAction); no caller-supplied foreground flag |
| POST /bff/logout | Existing durable LogoutRequested path; clear cookie only after local intent committed; maintenance completes upstream without cookie |
| POST /bff/password/change | Existing accepted password mutation; bounded UI pending/login retry, no unsafe automatic command replay |
| POST /bff/recovery/enroll | Existing password proof and one-time code response; acknowledgment is UX only, not a new server issuance guarantee |
| POST /bff/password/reset and complete | Uniform initiation and two-factor acceptance; pending completion resolved through bounded new-password login attempts |

Routes are to be formalized in bff-browser.openapi.yaml; none are current public API.
A session read may internally refresh but the browser never calls Identity refresh,
handles token generations or retries a consumed token. User-action routes are an
activity convention, not proof of a human. Polling uses background only. BFF_BUSY
returns Retry-After and a bounded retry policy; no immediate retry loop. Retry of
state-changing commands requires their existing operation/idempotency semantics.
BFF_UNAVAILABLE preserves/terminalizes sessions according to the core classification,
not a frontend interpretation of generic proxy statuses. RejectAccess handles an
Identity access 401 using matching session identity before local terminalization.
Logout lock-timeout before intent must not be presented as successful server logout.

Errors are stable code fields plus machine-readable metadata (e.g. retry seconds),
never translated human strings. Translate in the frontend, with a generic unknown-code
fallback. Tests assert status/code and side effects, not a localized sentence. A
request locale cannot alter proof verification, admission or error uniformity.

## Internationalization and input

/fa/... and /en/...; /bff stays outside locales. Precedence: explicit URL/selection,
then saved locale preference, then supported Accept-Language match, then fa.
Configure next-intl localeCookie explicitly: name=smartcore-locale, secure=true,
sameSite=lax, path=/, maxAge=15552000 (180 days), no Domain; not an auth cookie and
not HttpOnly so an explicit client language switch can update it. Values restricted
to fa/en. NEXT_LOCALE is not forwarded or used as an implicit fallback. Test a language
switch followed by unprefixed navigation through the proxy preserves that preference;
unknown/duplicate locale cookies are ignored. Only
unprefixed page entry redirects; prefixed URLs are not overridden. Public equivalents
have canonical/hreflang metadata. Locale negotiation excludes /bff, /_next and assets.
Direction: fa RTL, en LTR. Use logical Tailwind spacing/alignment and test keyboard,
focus, accessible labels and mobile layouts in both directions. ICU message catalogs
include validation/errors, empty/loading states and accessibility text. CI checks
message-key parity and formatting; no user-visible hardcoded text outside catalogs.

Normalize Persian/Arabic digits on both form and BFF server, with strict purpose-specific
validation. Phone numbers normalize to E.164 before budgets/Identity forwarding;
Iran-only (+98) delivery allowlist remains independent of the UI locale. Existing
Identity normalization/idempotency binding must remain consistent. Strip only approved
recovery-code display separators, invariant uppercase and enforce exact remaining
hex length. Do not normalize/rewrite passwords or treat arbitrary Unicode as an OTP.
UTC storage; fa Persian calendar with numberingSystem=arabext, en Gregorian with
numberingSystem=latn explicitly. Use IANA Asia/Tehran, not host timezone or a hand-coded
DST rule. Pin/update supported runtime/container tzdata and test identical Node/.NET/
browser results for known winter and summer 2026 instants (both +03:30); a mismatching
runtime fails acceptance rather than silently falling back. Account/appointment
timezone is explicit and independent of locale.
Forms use password autocomplete=current-password for login/current proof and
new-password for new/repeated passwords; OTP uses type=text, inputmode=numeric,
autocomplete=one-time-code and preserves leading zeroes. Phone input uses type=tel
and autocomplete=tel; normalize server-side as already specified. Do not disable
password-manager paste/autofill. Numeric display localization never rewrites password
values. Font files are local and licensed. Adding a third language also requires routing,
dir/format/font configuration and acceptance checks, not just a message file.

Notification locale is a separate persistence/blueprint decision before real delivery:
freeze an allowlisted language/template version in durable delivery intent; anonymous
registration needs a validated locale before Person exists. Define a profile preference
owner before adding a Person field. Do not mutate the current Person schema speculatively.
Measure Persian/Arabic encoding/segment count in SMS spend estimates. Multilingual
business content remains a later business-model decision; no automatic translation.

## Cross-tab session transitions

Use BroadcastChannel with event types only (login/logout/session-lost); never send
cookies, CSRF tokens, proof or profile data. After committed logout, the initiating
tab clears private UI state and broadcasts; listeners immediately clear private state,
cancel obsolete reads and invalidate their in-memory CSRF token. Discard responses
from a previous UI auth generation so a slow /me cannot restore logged-out UI. Login
listeners reread current session and fetch fresh CSRF instead of trusting the event.
Recheck on focus/visibility/pageshow and use a non-secret storage event fallback if
BroadcastChannel is unavailable. A tab event is UX coordination, not server revocation:
the BFF row state is authoritative. Pre-intent BFF_BUSY must not claim logout succeeded.

## B1–B3 delivery order

| Slice | Included | Done criterion |
| --- | --- | --- |
| B1 | Contract first, HTTPS proxy, two BFF hosts, csrf/login/me/logout, precreated test account, fa/en shell, security headers | Browser tests prove session isolation, Origin/CSRF rebinding, two-host single-flight, multi-tab logout and healthy localized UI |
| B2 | Registration/setup/OTP and protected inbox fixture | Real browser registration reaches Ready then login; expired/wrong OTP and inbox isolation controls pass |
| B3 | Recovery enrollment/reset/password change and one-time code UX | Full browser account journey below passes, including post-reset replacement enrollment |

B1 does not wait for OTP UI/provider fixtures. Seed its account through existing
trusted test fixtures outside the browser/public origin, never via a public bypass.
B2 builds the OTP fixture before its browser flows; B3 reuses it. Overall stage B is
complete only after B3, but each slice has a separate demonstrable CI outcome.

### Ordered B1 work and acceptance

| Order | Work | Acceptance |
| --- | --- | --- |
| 1 | Commit/review bff-browser.openapi.yaml with csrf/login/me/me-action/logout, cookies and machine errors | Examples and negative schemas pass; browser generation plus host-response validation consume this exact file |
| 2 | Set up pinned Next/TypeScript/next-intl shell and .NET host project | Reproducible builds; no trusted Identity keys/code in frontend; fa/en message-key parity |
| 3 | Wire private Identity, PostgreSQL 17 and existing BFF core/maintenance into two hosts | Isolated seeded account works; shared Data Protection keys; no new refresh/journal ownership |
| 4 | Configure HTTPS ingress and cookie/header allowlists | Only locale reaches Next; session/antiforgery reach BFF; unknown cookies and spoofed trust headers are dropped |
| 5 | Add custom cookie authentication, antiforgery and Origin checks | Bootstrap anonymous token, login-bound new token, logout then second login: all old session/prelogin tokens rejected; rejected requests dispatch nothing |
| 6 | Add contract-driven login/me/logout handlers | Safe DTOs/no tokens, correct cookies, local logout intent and maintenance semantics; response-schema checks pass |
| 7 | Build fa/en client forms/session shell and cross-tab handling | Locale/dir, autofill and error codes correct; second tab immediately clears private UI; delayed /me cannot resurrect it |
| 8 | Apply CSP/header policy and deterministic time formatting | Production Next hydration works with nonce CSP; fresh header/HTML nonce pair; winter/summer Tehran and explicit en Latin digits pass |
| 9 | Run Playwright through real HTTPS ingress across two hosts | Exactly one rotation for parallel reads; negative CSRF/Origin/cookie tests and outage/busy behavior pass; publish safe evidence |

No host code precedes task 1. Contract review is a concrete implementation prerequisite,
not a requirement to ask for another permission. Contract revision 0.1.1 has been reviewed. Task 2 now has a buildable shell/host;
task 3 has core and shared-key wiring, with seeded account integration still open.
Task 4 development HTTPS ingress is now implemented; see HTTPS_INGRESS.md.
Task 5 guards are now implemented; fresh CI evidence is tracked in BFF_BROWSER_GUARD.md.
Tasks 6–9 have private implementations and an actual account/browser acceptance
suite; fresh CI evidence is tracked in BFF_BROWSER_ACCOUNTS.md. B2 is privately verified in BFF_BROWSER_REGISTRATION.md; the complete private B3/Stage B journey is verified in BFF_BROWSER_RECOVERY.md.

## Browser CI acceptance

Run Next.js, two real BFF hosts sharing PostgreSQL 17, Identity and local HTTPS proxy.
Use an isolated database and fixed diagnostic keys supplied only to the CI fixture.
Read OTP over direct loopback via existing protected Development inbox; Playwright
page/JavaScript never sees its key or raw token fixtures. No external SMS provider.
Named fault fixtures must be development/testing-only and not proxy accessible.

| Scenario | Observable assertion |
| --- | --- |
| Full account journey | Registration Ready, login, enroll/acknowledge recovery code, BFF-triggered rotation, logout, reset, new-password login and replacement enrollment |
| Single-flight | Parallel session reads routed across two BFF hosts produce exactly one Identity rotation; consumed predecessor is not sent again |
| Session boundary | No Identity access/refresh token in browser response/storage/HTML; HttpOnly __Host cookie attributes correct; Next public render causes no authenticated call |
| CSRF/Origin (B1) | Missing/incorrect token and missing/null/foreign Origin reject before upstream work; login -> logout -> login rejects previous anonymous/session tokens, even for the same Person |
| Antiforgery cookie (B1) | __Host-smartcore-antiforgery Secure/HttpOnly/Path=/ and no Domain; token minted by one host accepted on the other |
| Cookie allowlist (B1) | Inject an unknown future cookie alongside session/antiforgery: Next receives only validated locale |
| Headers/CSP (B1) | Header values on success/error match policy, fresh nonces differ across HTML responses, iframe embedding blocked and production hydration has no CSP violations |
| Multiple tabs (B1) | Logout clears second-tab private state; old in-flight response discarded, old CSRF rejected and no server access restored |
| Busy/ambiguous outcomes | Verified NotProcessed preserves session, Retry-After bounded handling; uncertain refresh denies reuse and requests login; no frontend status-based retry |
| Logout outage | Durable local intent blocks ordinary access, cookie cleared after intent, cookie-free maintenance completes later; pre-intent busy does not claim success |
| One-time code | Code displayed once, acknowledgment required to proceed, no code in persistence/logs; lost enrollment response requires a new operation, not replay display |
| fa/en | Registration/login render expected lang/dir, switch preserves explicit locale, localized errors by code, mobile layout and digit normalization work |
| Inbox isolation | Wrong key/disabled flag/non-loopback rejected; Production startup rejected today; future production host omits routes even with diagnostics configured |
| Contracts | Generated browser/server clients reproduce from pinned schemas, build fails on drift; accepted Platform 1.2.2 provenance checked before release |

Use a bounded development fixture to make access refresh due; do not sleep twelve
minutes or weaken production TTLs. Assert rotation through protected non-secret
counters, not a browser refresh token. Disable/redact traces/screenshots/network logs
while OTP/password/recovery material is present; retain safe failure artifacts only.
Done means this actual browser suite passes in CI and the journey is demonstrable.
No existing C# count substitutes for it. Target-server npm/package retrieval, container
images, CI tooling and asset access must be checked separately before deployment;
no VPS access or connectivity success is presumed. Self-host assets and use no
Vercel-exclusive services in this slice.

Sources consulted 2026-10-11:
https://learn.microsoft.com/aspnet/core/security/anti-request-forgery (identity-bound
antiforgery) and https://nextjs.org/docs/app/guides/content-security-policy (nonce
requires dynamic rendering); https://next-intl.dev/docs/getting-started and
https://next-intl.dev/ (routing and ICU support). Existing runtime scope/evidence:
[PR_STACK.md](PR_STACK.md). Public-user gates: [RELEASE_GATES.md](RELEASE_GATES.md).

B1 draft contract is now [bff-browser.openapi.yaml](../../contracts/bff-browser.openapi.yaml).
It covers exactly five paths, before any host code. Schema/example checks are
documentary candidate evidence. Infrastructure HTTP checks validate scaffold
responses against this file; authenticated handler/browser validation remains open.
Primary configuration references:
https://next-intl.dev/docs/routing/configuration and
https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview .

B1 tasks 2/3 progress and explicit remaining work are tracked in
[BFF_BROWSER_HOST.md](BFF_BROWSER_HOST.md). Contract review 0.1.1 is complete;
Infrastructure build/HTTP checks are distinct from full browser acceptance.
