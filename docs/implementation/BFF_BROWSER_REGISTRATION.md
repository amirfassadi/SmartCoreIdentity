# B2 private browser registration candidate

This additive Development/Testing candidate is disabled by default. Set both
`Bff__AccountsEnabled=true` and `Bff__RegistrationEnabled=true` for private server
acceptance, and build Next with `NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED=true` and
`NEXT_PUBLIC_BFF_REGISTRATION_ENABLED=true`. Production startup/public binds are
still rejected. No release, main merge, SMS provider or VPS change is authorized by
these switches. B3 browser enrollment/reset/password change remains separate work.

## Contract and proof ownership

`contracts/bff-browser-registration.openapi.yaml` extends the unchanged five-path
B1 contract. `tools/bff_browser_contract.py` composes them, rejecting any replacement
of B1 paths/components. The generated client includes the composed DTOs. The B1
validator continues to check its original contract and negative fixtures. B2 checks
validate the composed OpenAPI, exact five additional routes, forbidden internal
properties, proof-flow boundaries and 3600-second rate limits. B1 stays capped at
60 seconds; B2 never clamps a contact-hour limit. Identity now returns Retry-After
60 for registration transport admission and 3600 for the rolling contact-hour cap.
These are conservative wait periods, not guarantees of admission or automatic retry.

All B2 POSTs use the existing ordered size/media/Origin/principal/CSRF/strict-JSON
checks before any Identity work. Only anonymous callers may register. Disabled B2
routes remain 404. The dedicated adapter dispatches only start/verify/resend/setup/
complete, with a five-second deadline, 32 KiB response bound, no redirects, cookies
or automatic replay. Private errors, trace IDs, PersonId/registration IDs, tokens,
binding secrets and challenge identifiers are never serialized to the browser.

The tab generates a 32-byte Web Crypto `flowId` kept only in memory. It correlates
requests and explicit retries; it is not authentication authority. Separate keyed
server purposes derive binding/start/complete idempotency keys. The first start
response stores the private verification ID, binding secret, absolute expiry and
flow ID in a Data Protection authenticated/encrypted HttpOnly
`__Host-smartcore-registration` cookie. Both BFF hosts use the existing encrypted
shared ring. Cookie attributes are Secure, HttpOnly, SameSite=Lax, Path=/, no Domain;
expiry cannot slide or exceed the private proof window (at most 11 minutes from
issuance). Setup replaces it with the independently bounded setup proof context.
No password or OTP is put in that cookie. The cookie is not a session, is not a
server revocation journal and cannot extend Identity's proof expiry/attempt budgets.

Every follow-up must match the tab's flow ID in constant time before Identity
work. Another tab overwriting the shared context fails closed; the first tab must
restart. Web Locks coordinate BFF cookie responses through headers as in B1. No
registration state/proof is broadcast or persisted in local/session storage. A
reload intentionally loses the flow ID and requires a new start. Completed context
expires naturally; authenticated requests are rejected regardless of that cookie.
Ingress passes the new cookie only to BFF and never Next/SSR. Private inbox/operator
headers still cannot cross ingress.

## User journey and retry boundaries

The fa/en form preserves the existing Iranian mobile normalization and leaves
passwords unchanged. Registration password length is 15–128 UTF-16 units; OTP
accepts local Persian/Arabic digits and normalizes them to six ASCII digits.
Successful start removes the password input. An ambiguous start can be retried
explicitly with the same flow and unchanged fields: Identity replays without extra
delivery or expiry extension. Changed fields conflict. There is no automatic retry.

Resend uses the same proof and a visible 60-second wait; Accepted is generic and
does not assert delivery. Identity enforces resend limits/cooldown and expires at
the original deadline. Wrong/expired proof shows a generic localized error.
Verification can return PendingCredential or Ready; it never creates login tokens.
Ready offers separate sign-in. Pending offers a manual readiness check with the
same code or separate sign-in; checks consume Identity's shared five-attempt proof
budget, so there is no polling or unbounded replay. The server remains authoritative
when browser clocks differ.

Only accepted ownership proof can reveal SignInRequired or SetupRequired. Setup
requires the original proof again and requests a distinct code; its completion uses
a fresh password candidate. CandidateSelected/ExistingWinner/Undetermined stay
explicit. ExistingWinner tells the user to sign in with the existing password; no
losing registration candidate replaces a Ready password. There is no automatic
sign-in after either completion outcome.

## Acceptance

`tools/check_bff_registration_contract.py` checks documentary boundaries.
`scripts/bff-registration-smoke.py` enables B2 on the same isolated PostgreSQL 17,
real Identity/two-BFF/TLS stack as B1. Identity's normal provisioning worker is
paused for this fixture; private CLI provisioning makes Pending/Ready transitions
deterministic without changing production services. Its stdin-only CLI reads
challenge IDs and drives provisioning in the disposable database; the protected
private inbox provides OTPs outside browser origin. No diagnostic HTTP route exists.

HTTP controls cover lost-start replay/no duplicate delivery, strict DTO/guard
attempt preservation, malformed/mismatched/cross-tab contexts, resend, wrong/expired
proof, pre-proof identical start shapes, proof-gated next actions, the real hour
cooldown, setup replay, distinct setup code, CandidateSelected and ExistingWinner,
winning-password login, and outage cookie preservation. Responses are checked
against the exact composed contract, including every cookie attribute/action.

Chromium uses production Next and verified TLS with only the ephemeral certificate
SPKI trusted. Private pipe messages supply protected-inbox OTP/provisioning without
exposing an inbox key or ID to the browser. It exercises a lost start response and
explicit retry, Persian OTP/RTL mobile, Pending/Ready, separate English login, CSP,
HttpOnly proof and absence of private values in storage. No traces, screenshots,
network dumps or secret-bearing diagnostics are retained. The private Identity process is restarted between the independent HTTP and browser
suites so the deliberate HTTP admission controls do not consume Chromium's real
30/minute registration transport budget. No limiter is disabled or broadened.
Native builds and static checks passed locally; full live CI evidence will be recorded after those jobs pass.

## Verified B2 acceptance — 2026-10-12

Code `6c6dd7dbac7a48a13eafba7ca1b4feb490890b8e` passed
[browser 38302098613](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38302098613),
[backend 38302099674](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38302099674)
and [documentary 38302098635](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38302098635).
The browser job log was inspected: actual B1, B2 HTTP, B2 Chromium and B2 outage
controls passed. Its same-tree integration `f6ca21988cf440f0a99de1a0bbba51c2ec45d537`
also passed all four workflows, including
[full preflight 38302102154](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38302102154):
**39 passed, 0 failed, 0 skipped**. No default feature flag was enabled.

The separate full-preflight run on the review head failed in the existing native
harness and recovery smoke while its B2 suite passed. The successful integration
run has the identical full source tree; this is not a claim that failures disappear
on every runner. Follow-up diagnostics expose only enumerated test phase/type
markers and continue to fail the run without printing raw credential-bearing logs.
B3 and production/public-release acceptance remain separate gates.
