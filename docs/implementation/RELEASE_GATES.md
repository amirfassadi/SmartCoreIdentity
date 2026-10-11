# Identity release gates

Status: accepted direction; implementation/evidence still required. This is the
single current list of public-user release requirements. Historical verification
reports describe their tested revisions and do not close these gates by themselves.
The development/testing-only startup boundary remains enforced. No public release,
main merge, production/VPS change or real SMS purchase is authorized by this file.

## Before real users (including closed beta)

| Gate | Required evidence | Current disposition |
| --- | --- | --- |
| Canonical contract | Platform PR #11 merged first; Identity pin/provenance compared to canonical Platform schema 1.2.2; applicable OpenAPI validation passes | Platform merge/provenance open |
| Browser account path | Same-origin BFF host and Playwright flow: registration, login, refresh, logout, reset, login with new password and replacement recovery enrollment | Host/browser E2E not implemented |
| OTP delivery | Real SMS adapter behind a provider-neutral interface and durable notification queue; destination-country delivery and sender/template registration verified | SMS selected first; Iran confirmed; provider undecided |
| SMS pumping | Allowed-country list enforced before enqueue/send; destination and trusted-subject budgets independent of Person; aggregate spend ceiling, provider hard cap and delivered alerts; concurrent-instance enforcement tested | Required before provider activation |
| Delivery ambiguity | Durable attempt journal, explicit outcomes, safe provider idempotency/status reconciliation, duplicate/reordered webhook tests | Design below; adapter/worker pending |
| Operational alerts | Circuit opening/probe losses, maintenance failures and account reset limits exported and delivered to an operator; escalation/runbook tested | Signals exist; delivery pending |
| Basic load | Browser/BFF/Identity baseline with concurrency, latency/error rate, pool/queue usage and failure-only reset timing overruns; predefined pass thresholds | Controlled regression checks exist; acceptance pending |
| Failover and restore | Real replicated-database failover exercises dispatch-journal durability/no-reuse; encrypted backups and actual restore test | Process-kill checks exist; replicated failover/restore pending |
| Contact release | Manual ownership-safe policy/runbook for recycled phone numbers, disputed ownership and duplicate-registration lockout; audited operator workflow, no contact-only takeover | Pending |
| LoginFailed retention | Chosen retention duration, cleanup/aggregation, growth limit and contact-data minimization; attacker-driven volume test | Pending |
| Deployment | TLS, secure cookie policy, secret provisioning/rotation, restricted operator routes, health/metrics and startup configuration reviewed | No production deployment |
| Product/legal | Recovery-code loss warning, one-time display acknowledgment, applicable privacy/terms and retention notice for chosen market | Pending; initial market Iran |

Budget values, spend amounts, alert destinations and load/failover pass thresholds
must be recorded before this list is marked complete; naming a gate is not evidence.
The first real-user release is a closed beta after these gates, not an exception
to them. Initial users are in Iran; the first allowed-country policy is Iran-only
(+98), subject to implementation/testing before sending. SMS selection follows the
users' country, not the VPS location.

## Conditional or later work

Person eligibility transitions can wait only while no disabling/reactivation route
exists. The current Active-only database CHECK is executable protection. Introducing
such a capability requires an accepted mutation protocol with epoch advancement,
session closure and tests preventing old-session resurrection in the same delivery.
Additional hardening may enter backlog unless it enables account takeover, data loss,
mass logout, violates an accepted contract or breaks the main product path.

## SMS adapter boundary and durable outcomes

Use a provider-neutral interface behind the existing durable delivery outboxes;
registration, setup and reset delivery tasks already have stable IDs. The journal
uses the delivery operation ID as the provider client reference, not a newly created
reference on every retry. Person may not exist yet, so neither reference nor spending
protection can depend solely on Person. Do not put plaintext OTPs, credentials or
provider secrets in journal diagnostics or webhook logs.

Before any external send, commit an attempt record with operation/client reference,
attempt ID/number, chosen provider, time, validity deadline and dispatch-intent state.
A restart after that commit must assume a possible send, even if the process died
before the socket call. Keep this evidence independent of a transaction that could
roll back after the provider accepted. Enforce one dispatch/reconciliation owner;
never expire a lease into a second uncontrolled send.

The interface must distinguish these outcomes:

| Outcome | Meaning | Allowed continuation |
| --- | --- | --- |
| Accepted | Provider accepted the message, not proof it reached the handset | Save provider message ID and await/reconcile delivery receipt |
| Definitively rejected | Provider positively reports no acceptance, e.g. invalid destination; local country/budget rejection performs no send | Record bounded safe reason; terminal permanent rejection, no automatic resend |
| Unknown | Timeout, response loss, crash after intent or untrusted intermediary response | Reconcile by original client reference, or resend only under the provider's original idempotency key |

A provider's explicit transient **not accepted** response may be classified separately
for bounded backoff only where its documented contract proves no message was accepted.
HTTP status alone is insufficient. Expired challenges never get sent or retried.
Provider failover after an unknown result is also a possible duplicate and is blocked
until the original acceptance is resolved; a backup is not an ambiguity bypass.

For unknown results, honor the provider's idempotency retention window and request
binding. If that window expired, status lookup (when reliable) is the only automatic
route. If neither mechanism exists, stop automatic OTP dispatch; the user can request
a new challenge through the existing limits. Creating a challenge must not silently
extend an old challenge's validity or bypass destination/subject/spend budgets.

Signed webhooks are authenticated before state changes (provider-defined signature,
body, freshness/replay validation). Deduplicate provider event IDs, bind message ID
and operation reference, and handle duplicates/out-of-order delivery without repeating
send. Receipts may arrive before the acceptance response; reconcile without losing
that evidence. A delivery receipt is not successful OTP verification. Acceptance,
delivery and proof consumption remain separate facts.

Provider selection priority: reliable idempotency or status lookup by original client
reference is a prerequisite ahead of price/delivery ranking. Then compare delivery on
initial-market operators, OTP routes, sender/template registration, authenticated
receipts, spending controls and a backup with an explicit ambiguity policy. Initial market is Iran (owner-confirmed 2026-10-11); provider remains open. No
vendor-specific adapter is selected or implemented here.

## Development inbox and browser acceptance

Use only the opt-in Development diagnostic inbox, a dedicated key and direct loopback
connection. Read codes through the CI runner/server fixture; do not expose diagnostic
keys to the browser bundle or forward inbox routes through the public BFF/proxy. There
is no real SMS charge in this path. Existing startup rejects Production entirely;
this is not yet a production-host endpoint isolation test.

The browser-host delivery must add negative controls for disabled inbox flag, wrong
key, non-loopback and non-Development/Production. When a production host is permitted,
assert the inbox routes are absent/rejected even if diagnostic configuration is supplied.
The Playwright main-path test reads OTP from the protected fixture and exercises
registration -> login -> BFF refresh -> logout -> two-factor reset -> new-password
login. Verify a non-secret generation/call-count fixture proves BFF refresh happened;
no raw refresh token enters browser storage. Add one-time recovery-code acknowledgment
and replacement enrollment after the successful reset login. No production diagnostics
are introduced by a test requirement.

Accepted contract scope means the applicable registration/authentication OpenAPI and
pinned Platform 1.2.2 contract. Generate the trusted .NET-side Identity client from those
contracts. Next.js performs no authenticated SSR reads; browser code targets a
separate same-origin BFF contract; it must not call
Identity refresh or send trusted BFF headers. Declare/generate that BFF contract in
stage B and fail CI on generation/schema drift. These are future acceptance tests,
not newly executed CI evidence.

Stage B is specified in [BROWSER_SKELETON_SLICE.md](BROWSER_SKELETON_SLICE.md).
Explicit Origin and antiforgery checks also protect anonymous browser mutations,
including login/register/reset; same-origin routing and SameSite alone do not close
this gate. fa/en UI support does not expand the Iran-only SMS market. Server input
normalization, self-hosted fonts and frozen notification locale/template intent are
required as specified in the slice before their corresponding path is released.

Stage B now lands as B1 (session/proxy/CSRF with seeded login), B2 (registration/OTP),
and B3 (recovery/reset/change). B1 specifies a custom opaque-cookie authentication
handler before antiforgery, per-BFF-session identity binding, __Host antiforgery
cookie rotation and fresh frontend tokens on auth transitions. Locale-only cookie
allowlisting to Next, nonce CSP/dynamic uncached HTML and other security headers,
cross-tab logout and timezone/digit regression tests are B1 acceptance requirements.
The browser OpenAPI precedes host code and validates actual host responses as well
as generated clients. Full stage-B release evidence still requires all three slices.
