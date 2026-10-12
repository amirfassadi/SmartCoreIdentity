# B3 browser recovery contract candidate

The additive contract defines enrollment, password change, uniform reset initiation
and two-factor reset completion. This is a private Development/Testing candidate,
implemented for private acceptance; live CI and release approval remain separate. B1 and B2 contract
paths/components must stay unchanged. Runtime requires explicit recovery opt-in;
public/Production startup gates remain enforced.

Enrollment requires the authenticated opaque BFF session, current password and
explicit loss-risk acceptance. Issued returns the one-time 128-bit code in volatile
UI only. AlreadyIssued never returns it again. Acknowledgment only hides the local
code; it cannot prove the user saved it. An ambiguous enrollment must not be
silently replaced/replayed. Explicitly enrolling a replacement invalidates the
previous recovery code and requires new consent. No storage, SSR, telemetry,
clipboard automation, downloads, screenshots or traces retain the code.

Change requires current password and a 15–128-unit new password. The existing
Identity command first fences issuance and closes all sessions. Browser Accepted
means durable intent; it does not claim Credential replacement completed. BFF must
commit local logout before clearing session/antiforgery cookies and acknowledging.
A failure after upstream acceptance stays ambiguous; no automatic command replay
or success claim. Fresh independent sign-in resolves pending work, with admission
limits and manual actions instead of polling credential commands.

Reset initiation is anonymous and uniform for enrolled/unenrolled/unknown contacts.
A server-owned encrypted HttpOnly __Host proof cookie carries the private challenge,
binding and absolute expiry. Follow-up tab flow ID must match before upstream work.
Both OTP (new challenges eight digits; bounded legacy six-digit acceptance) and a
pre-enrolled 32-hex recovery code are required. Normalize only OTP/recovery spelling,
never passwords. Completion consumes/reserves that recovery verifier according to
the existing authoritative workflow; it creates no login session. After new-password
login, enrollment is required again. Operators and private status routes never cross
ingress or become browser APIs.

The native reset service intentionally uses a uniform unauthorized failure during
its persisted 15/30/60-minute proof cooldowns. It does not supply the exact cooldown
or eligibility in response metadata. BFF must preserve that nondisclosure rather
than inventing or clamping a Retry-After. Existing generic admission 429/503 retains
the B1 60-second contract. Wrong current password/invalid reset factors map to
browser proof errors and preserve cookies; they cannot be mistaken for terminal
session access rejection. Only an independently recognized private /me access 401
may terminalize the matching BFF session. Backend cooldown controls remain covered
by the native recovery suite, not a fabricated browser countdown.

Acceptance suite: real TLS/two-host HTTP proof/cookie controls and the
full Chromium journey from B2 registration through login, one-time enrollment,
logout, both-factor reset, pending/new-password login, replacement enrollment and
password change with old-session rejection. Protected inbox and private CLI worker
fixtures stay outside browser origin. Secret-bearing diagnostics remain suppressed.

## Private implementation and checks

Server opt-in is `Bff__AccountsEnabled=true` plus `Bff__RecoveryEnabled=true`.
UI opt-in is `NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED=true` plus
`NEXT_PUBLIC_BFF_RECOVERY_ENABLED=true`; both are false by default. The real B3
fixture also enables B2 registration. The dedicated adapter projects only safe
status/code fields and the intentional one-time recovery secret; private operation
IDs/version/trace IDs never escape. Each command uses a tab random flow and a
server-derived, purpose-separated operation UUID, bound to the current Identity
session for authenticated commands. Stable canonical-contact subject MACs bound
reset initiation/completion source admission across new challenges.

The reset proof cookie is Data Protection authenticated/encrypted using the shared
ring, bounded by private proof expiry with no sliding lifetime; it holds neither
password, OTP nor recovery code. Mismatched tab flow fails before Identity work.
Ingress forwards it only to BFF, never public SSR. The generated browser client
serializes cookie effects through response headers, including password-change
clears, and obtains fresh antiforgery after accepted local logout.

The UI explicitly requests new consent for replacement enrollment, hides the code
on acknowledgment/visibility change/unmount, and resets password/proof inputs after
accepted commands. Unknown enrollment outcome can be explicitly replayed with the
same flow; AlreadyIssued returns no secret, and replacement is a separate action.
There is no automatic replay or new-password login. Cross-tab session-lost events
contain only the existing transition type.

`scripts/bff-recovery-smoke.py` exercises real Identity, PostgreSQL 17, two BFF hosts
and TLS. It pauses only its private Credential worker and uses a stdin-only CLI
to deterministically reconcile accepted intents. No operator route/key enters
browser origin. HTTP checks include current-password denial/cookie preservation,
no-code enrollment replay, both factors, proof-flow/DTO rejection, pending fencing
and old-session rejection, reset receipt replay, new-password login, replacement
enrollment, password change, outage and local lock failure before upstream dispatch.
Chromium starts with B2 registration, forces access due through the private CLI,
and verifies one real browser /me/action causes exactly one native generation
advance while preserving the opaque cookie. It then tests lost one-time response/replay, fresh
replacement consent, one-time code isolation/acknowledgment, two-factor reset, fresh
login, replacement enrollment and Persian password change/cross-tab rejection.
Native builds, type checking, production build, schemas and ingress pass locally.
The exact successful live execution is recorded below.

## Verified complete private browser journey — 2026-10-12

Code `f3851af966ebe36d1e49287a404db0395582f292` passed all four workflows:

- [Browser B1/B2/B3 38306669824](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38306669824)
- [Backend 38306669984](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38306669984)
- [Documentary 38306669922](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38306669922)
- [Full local preflight 38306669827](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38306669827)

Jobs `115002840710` and `115002803091` logs were inspected. The native harness
retains **303 cumulative checks**, all five earlier API smokes and both controlled
reset timing gates. Full local preflight reports **41 passed, 0 failed, 0 skipped**;
nine focused preflight regression checks pass separately. Browser release builds
had zero warnings/errors. Actual B1/B2/B3 HTTP/Chromium, pre-dispatch lock failure
and private upstream outage controls passed. The complete browser journey proves
a single native refresh-generation advance with a stable opaque cookie before
one-time enrollment, reset, new-password login, replacement enrollment and change.

The same-tree integration is `d8f79e456e1dc69642feea0993f78ae501eb27d3`. Its browser,
documentary and full-preflight jobs passed independently. The first separate
backend run hit one legacy wire-timing sample at 355.21 ms against the unchanged
350 ms maximum; the reset service itself measured 217.68 ms and the rest was
pipeline overhead. Only that failed job was retried, with no threshold or test
relaxation: [backend 38306672999, attempt 2](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38306672999) passed.
All four integration workflows now pass on the identical source tree. This remains
controlled regression acceptance, not production/load or constant-time certification.

This closes the **private Stage B** implementation/Chromium journey. Default flags
and public/Production startup gates remain disabled. Canonical Platform/Identity
merges, real SMS delivery/spend controls, delivered alerts, load/failover acceptance,
operational/contact-release policy and market privacy/retention still gate real
users. No main merge, VPS/service/database change or provider send occurred.

Live wire-contract failures retain route/method/status only: schema exceptions cannot
print the one-time recovery code, antiforgery token or raw headers. The documentary
validator/negative fixtures remain unchanged. A focused failure regression verifies
that formatting the live exception cannot recover the rejected proof value.
