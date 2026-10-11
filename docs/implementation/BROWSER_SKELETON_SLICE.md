# Stage B — bilingual browser account skeleton

Status: agreed architecture direction, implementation slice specification; no host,
frontend, generated BFF client or browser E2E is delivered by this document.
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
The reverse proxy strips the auth cookie before page requests reach Next.js, since
this slice needs only the optional non-secret locale preference there.

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
never reuse that trusted adapter as the browser client. Generated output is checked
against committed contracts; drift fails build. Compile/build both runtimes in CI.

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
Reject missing/null/wrong Origin for these browser routes. No credentials in URL.

GET /bff/csrf issues the non-secret request token (session binding handled by the
antiforgery framework); the browser submits X-CSRF-Token on POST. No-store on all
account/BFF material; no shared cache, logs or tracing payload capture for password,
OTP, recovery code or cookies. A locale cookie is separate and is not authentication.

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
then saved locale preference, then supported Accept-Language match, then fa. Only
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
UTC storage; fa Persian calendar and en Gregorian display; timezone explicitly
Asia/Tehran for initial-market appointment/account display, not inferred from server.
Font files are local and licensed. Adding a third language also requires routing,
dir/format/font configuration and acceptance checks, not just a message file.

Notification locale is a separate persistence/blueprint decision before real delivery:
freeze an allowlisted language/template version in durable delivery intent; anonymous
registration needs a validated locale before Person exists. Define a profile preference
owner before adding a Person field. Do not mutate the current Person schema speculatively.
Measure Persian/Arabic encoding/segment count in SMS spend estimates. Multilingual
business content remains a later business-model decision; no automatic translation.

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
| CSRF/Origin | Missing/incorrect token and missing/null/foreign Origin reject login and authenticated mutations before upstream work |
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

Sources consulted 2026-10-11: https://next-intl.dev/docs/getting-started and
https://next-intl.dev/ (routing and ICU support). Existing runtime scope/evidence:
[PR_STACK.md](PR_STACK.md). Public-user gates: [RELEASE_GATES.md](RELEASE_GATES.md).
