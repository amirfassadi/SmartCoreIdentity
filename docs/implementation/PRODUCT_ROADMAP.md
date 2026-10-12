# Product delivery path

Accepted direction: a business owner can eventually launch their own business site.
Kimia is an example service business, not a platform-specific tenant model. Every
stage has a demonstrable completion criterion; feature blueprints precede code.
No stage below claims completion or authorizes a main merge/production deployment.

## Shared frontend foundation

One server-rendered web application with three surfaces: account management,
business-owner dashboard, and public booking site. Keep it same-origin with BFF;
use an opaque __Host- session cookie with Secure, HttpOnly, Path=/ and no Domain,
and explicit CSRF/Origin controls for state-changing actions. The browser never
receives Identity refresh tokens and never runs refresh itself. The shared core owns
layout, localization, validation and error presentation. Mobile-first; fa/en localization with RTL/LTR is included from the first slice.

Generate the server-side Identity client from authentication.openapi.yaml and the
registration contract. The browser consumes the BFF's browser contract, which must
be defined before generation; the Identity OpenAPI alone does not describe browser
cookie/CSRF flows. CI must detect generated-client and contract drift.

Account UX includes registration/login/logout/reset/password change, recovery code
shown once with explicit acknowledgment (without logging/persisting plaintext),
replacement enrollment after reset login, and bounded BFF_BUSY/Retry-After handling.
Background polling never counts as foreground. Owner tools include businesses,
services, hours and appointments. Public pages expose customer booking.

## Stages and completion criteria

| Stage | Scope | Demonstrable done criterion | Status |
| --- | --- | --- | --- |
| A | Platform #11 then Identity #1/#2/#4/#5/#6/#7; retarget/rebase as required | All stages on canonical main with successful CI after each base change | Draft stack prepared, not merged |
| B1 | Contract-first host/proxy, seeded login/me/logout, csrf and fa/en shell | Browser session/CSRF/Origin and two-host single-flight tests | Private Chromium/HTTP B1 verified; exact revisions in BFF_BROWSER_ACCOUNTS.md |
| B2 | Registration and protected OTP inbox | Browser registration Ready then login; proof/inbox rejection checks | Not implemented |
| B3 | Recovery enrollment/reset/password change | Complete Playwright journey through new-password login and replacement enrollment | Not implemented |
| C | Real-user release controls, SMS, operations and deployment | Closed beta after all evidence in RELEASE_GATES.md is accepted | Open |
| D | Business registration: one-page blueprint, backend, owner dashboard | Owner creates a business from start to finish; correct Organization/Membership ownership | Future |
| E | Availability, booking, SMS confirmation, owner calendar, cancellation | Example service business receives a real reservation with tested conflict/cancellation behavior | Future |
| F | Templates, subdomain/custom domain, publishing | Owner launches a site without a developer | Future |
| G | Monitoring, FailedClosed/contact-release operator tools, support, membership handoff | Demonstrated audited operational recovery and authorized team handoff | Future; minimum release operations must already land in C |

Stage B can be developed against the reviewed integration contract in parallel with
A; integration does not bypass A, contract provenance or release gates. Each new
feature gets a concise blueprint (actor, command/query, invariants, storage ownership,
contract and acceptance flow) and its end-to-end test as part of done. Domain selection
must cover tenant resolution and cookie isolation before publishing multiple sites.

## Decisions still open

| Decision | Required input | Current position |
| --- | --- | --- |
| Frontend framework | Owner decision; team familiarity is not a constraint | Accepted: Next.js App Router + React + TypeScript; existing ASP.NET Core BFF remains session owner |
| Business-site domains | Initial hosting/domain plan and tenant isolation | Subdomain vs custom-domain rollout undecided |
| Initial users' country | Owner's initial market | Iran, confirmed 2026-10-11; provider selection remains open |
| OTP channels | Agreed product policy | SMS first for real users; email remains valid; both use test inbox in development |

Release requirements and SMS ambiguity/abuse policy have one current source:
[RELEASE_GATES.md](RELEASE_GATES.md). Dependency/tree evidence remains in
[PR_STACK.md](PR_STACK.md). CI counts are cumulative historical evidence, not proof
that future stages or browser/provider paths are implemented.

## Frontend choice (accepted; private B1 implementation)

Next.js App Router with React and TypeScript, with Tailwind CSS for the shared
responsive design system. Use server rendering for public sites without session reads. Account/dashboard
client components call /bff directly; Next.js does not perform authenticated SSR. This is a product choice
for the three surfaces, not an assumption about owner/team familiarity. Keep the
existing ASP.NET Core BFF and Identity; do not duplicate refresh/session ownership
in Next.js. A single HTTPS origin routes page requests to Next.js and /bff requests
to ASP.NET Core. Next.js receives only an allowlisted, validated locale cookie; all other cookies
are omitted on page requests.
Authenticated responses and one-time recovery material must never enter shared caches. Browser mutations retain BFF CSRF/Origin checks.

Tradeoff: an additional Node.js runtime and explicit proxy/session/cache isolation
to operate. Pin supported dependency versions when implementation starts.
Razor Pages would simplify hosting, but Next.js is the accepted basis for the future
interactive owner dashboard and customizable public site. The owner accepted this choice on 2026-10-11. The current private frontend and acceptance evidence are tracked separately in
BFF_BROWSER_ACCOUNTS.md.

Primary references consulted 2026-10-11:
https://nextjs.org/docs/app/getting-started/server-and-client-components and
https://nextjs.org/docs/app/guides/self-hosting . Next.js supports self-hosting;
Vercel hosting is not a requirement. Initial locale is fa-IR with RTL. Normalize
Persian/Arabic digits in phone/OTP inputs; store timestamps canonically and localize
date/calendar presentation separately. Provider selection must now assess Iranian
operator coverage and sender/template requirements without assuming any vendor's
idempotency/status support.

Stage B folder/proxy/browser-contract and fa/en test specification:
[BROWSER_SKELETON_SLICE.md](BROWSER_SKELETON_SLICE.md).
