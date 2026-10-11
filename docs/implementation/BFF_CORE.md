# BFF refresh core scaffold — 2026-10-10

The class library src/SmartCore.Identity.Bff implements the token-store/refresh-coordination slice of [BFF_REFRESH_DESIGN.md](BFF_REFRESH_DESIGN.md). It is called only by trusted server code. It does not expose HTTP/browser endpoints, serialize BffAccess to browsers, set browser cookies or implement CSRF/Origin routing. No production host or public onboarding is enabled. The opaque cookie value returned by Create is the future cookie secret, not a refresh token.

BffStore owns a separate schema/migration ledger (bff-database/001_bff_sessions.sql, 002_refresh_admission.sql and 003_refresh_circuit.sql and 004_circuit_recovery_logout.sql); it can use a separate database, and is not part of Identity's migration 001–011. Two application instances must use the same BFF database and BFF encryption/MAC keys, distinct from Identity's keys. The scaffold reuses the API project's database/crypto/token DTO dependencies; extraction of shared primitives is a future packaging choice, not a shared transaction or store. No new NuGet dependency is introduced.

Create seals the trusted login token bundle by BFF session/version, stores only a keyed opaque-cookie verifier and creates the fixed FK journal row in one transaction. GetAccess reads committed active access without row locking. It returns a RefreshDue signal for the host's bounded proactive scheduler; the library never starts detached background tasks. The host must call RefreshIfDue with its trusted activity classification around 80% of the issued access lifetime and for due foreground activity, even when cached access is still valid. Expired-access callers wait for a current successor; they cannot fall back to an expired token.

RefreshIfDue locks the session row, re-reads version/deadlines and journal, and uses the committed successor when another instance already satisfies the decision. A second connection commits the journal before exactly one Identity call. The owner stores the successor and attempt/version in its original transaction; cleanup uses attempt/version CAS. A crash, malformed/lost response, HTTP timeout or cancellation after dispatch leaves the durable marker. The current owner marks ReauthenticationRequired when it can commit within its reserved cleanup time; otherwise a later refresh owner recognizes exact committed success or irreversibly marks ReauthenticationRequired, erases tokens and never calls Identity with the abandoned predecessor. Already-valid cached access may still be used until that terminal state is committed or its remaining lifetime expires; this is the explicitly accepted bounded fast-path race, not permission to retry refresh. Logout now calls Identity before local erasure; Close remains local cleanup only. Browser hosting is not delivered.

The concrete IdentityRefreshHttp adapter owns a non-redirecting, non-retrying HttpClient, bounds response bytes and uses caller deadline cancellation. HTTPS is required; explicit development loopback HTTP is available for future native HTTP acceptance. The coordinator applies 5-second HTTP, 15-second owner (including a reserved 20% cleanup budget) and 17-second contender budgets; a PostgreSQL transaction cap covers wait plus owner time. Production transport credentials, signing/JWKS validation and hosting isolation remain release work.

BffActivity is a trusted server enum, never a raw browser assertion. Background keeps last_foreground_at unchanged; UserAction refreshes it only after successful successor commit. The browser host must classify allowlisted CSRF/Origin-protected actions and cannot map arbitrary headers, polling or a timer to UserAction. The token library cannot prove a human or enforce a route policy without that future host.

## Executable scope and limits

BffRefreshChecks is part of the native PostgreSQL C# harness. It uses two independent BffStore data sources/pools, actual Identity authentication/refresh transactions and a transport fault seam. It checks 20 parallel foreground decisions for exactly one rotation and no reuse, valid-access reads while rotation is paused, expired-access waiting longer than two seconds, five owner-loss stages, server-side PostgreSQL termination of the owning BFF connection, lost/malformed/timed-out responses, cancellation after dispatch, foreground after fresh background rotation, thirty-minute polling expiry, token erasure, definitive revocation excluded from uncertainty, and distinct-session uncertainty aggregation.

The transport seam calls the actual Identity service directly. Additional controlled loopback HTTP bridge checks exercise the concrete adapter: twenty parallel decisions on two instances send one authenticated HTTP refresh, an HTTP abort after the real Identity commit is never retried, and a redirect never forwards secrets. This bridge is not the complete Identity middleware pipeline. The initial runtime 779c6ab did not test OS process death. The admission follow-up below adds independent process peers and actual kill coverage. Browser cookie/CSRF endpoints, database failover durability, pool exhaustion and production load remain acceptance work before real users. The core has no autonomous purge or production retention decision. Runtime 779c6ab passed native PostgreSQL CI with 26 BFF checks within 224 total C# checks, 230 emitted-event validations and five existing API HTTP smokes. Exact SHA/run/job evidence and limits are in [VERIFICATION.md](VERIFICATION.md).

## Admission rejection contract (authentication subset 0.8.0)

Identity issues a `NotProcessed` receipt only for refresh requests
rejected before invoking `AuthenticationService.Refresh`: `429 RATE_LIMITED`, or
reserved pre-invocation `503 AUTHENTICATION_BUSY`. Refresh presently performs no
KDF, so its current overload rejection is the admission/rate boundary. Dependency
`TEMPORARILY_UNAVAILABLE` is never a receipt. Pre-invocation 403 BFF_CREDENTIALS_REJECTED and 400 VALIDATION_FAILED also receive this receipt; wrong key/subject and invalid BFF requests are configuration failures. A fresh `X-Refresh-Request` UUID,
status/code pair and retry delay are bound with domain-separated HMAC-SHA256 using
the independent Identity:RefreshReceiptKey (distinct from the BFF client credential). The adapter validates the bounded body and exact nonce. This
assumes a trusted Identity origin/TLS termination and confidential receipt key; it
is not protection from a proxy that holds that key. Unsigned proxy 502/503/504,
wrong nonce, invalid proof, redirect, lost response and malformed body remain
uncertain. HTTP status alone never authorizes predecessor retry.

After a valid receipt the owner CAS-clears only its journal attempt/source version
using connection B, preserving the session token bundle, version and foreground
evidence. B also durably stores the next retry deadline and rejection streak;
all instances enforce it under the session row lock. Return `BFF_BUSY` with the
core's `RetryAfterSeconds` (future browser host must emit `Retry-After`). Backoff
is 1/2/4/8/16/30 seconds, at least the signed 1–60 second Identity delay; a
successful successor clears this streak. There is no in-call retry loop. Cleanup
failure leaves durable dispatch evidence for conservative recovery.

Run `BffStore.Migrate` before serving requests: startup rejects PostgreSQL below
17 with an explicit `transaction_timeout` requirement. Separate BFF migration
002 adds admission retry evidence; Identity migrations remain unchanged. The
refresh path also checks the server version before setting transaction timeout.

Test-only peers run the built test executable in separate OS processes, with
credentials sent over redirected stdin rather than arguments/output. Each owns
its own pools and HTTP client. Twenty requests across two processes must make
one real Identity HTTP rotation. At each of the five fault boundaries the parent
forcibly kills the paused owner, then starts an independent recovery process.
This exercises OS death/connection release, not a thrown simulated crash. It
still uses one PostgreSQL server and a controlled Identity HTTP bridge: replicated
failover, browser host, production load and pool saturation remain release gates.

The core Logout/RejectAccess methods below supply upstream-first logout and Session-401 closure. A browser host must wire them before exposing routes.

Verified runtime `9a9da68b203036c2beaf8bb37cccc5c1c5616a01` passed native
PostgreSQL 17 CI with **44 BFF checks within 242 total C# checks**, 256
emitted-event validations and all five existing API HTTP smoke scripts.
Both independent-process contention and all five explicit OS-kill boundaries
passed. The real Identity middleware smoke also verified a signed rate-rejection
receipt and subsequent rotation of the unconsumed token. See VERIFICATION.md for
run/job provenance, the initial test teardown failure and the corrected historical
BFF subcount (26, previously reported as 25).


## Shared operational circuit (0.8.0)

Wrong BFF key/subject is 403 BFF_CREDENTIALS_REJECTED, separate from Session 401.
Receipt signing requires the independent Identity:RefreshReceiptKey; API startup
rejects a missing key or one equal to the BFF credential. Deploy API and BFF with
this shared receipt key before changing the development client credential. A
verified configuration receipt CAS-clears its known non-consumed dispatch,
preserves tokens and opens the deployment circuit. An unverifiable 403 opens it
but retains the unmatched journal; it does not authorize retry. In both cases
callers receive BFF_UNAVAILABLE, with a suggested 60-second retry delay.

Migration 003 stores one shared circuit per shared BFF database/deployment and
recent samples keyed by distinct BFF session. It opens immediately on a
verified configuration receipt, three distinct unverified 403 responses, or at least five distinct 400/401/403
rejections making up at least 50% of the last 30-second session samples. Repeated
errors from one session cannot fill that threshold. The triggering and later
sessions retain tokens; unmatched dispatches are quarantined. Before threshold,
individual definitive 401 still closes that session; the circuit cannot restore
sessions already closed. In-flight results already committed before opening also
remain committed. Ordinary cached valid access is still available.

Migration 004 enables one recovery probe per minute across instances. A successful
elected probe closes the circuit; no timer authorizes replay of uncertain dispatch
evidence. Trusted Reset can accelerate recovery after investigation but clears
only circuit samples/state, never journal evidence, Identity fences or tokens.
A NotProcessed claim with unverifiable nonce/MAC keeps its session's dispatch
quarantined; unverified 403 needs the distinct-session threshold, whereas other
unverifiable receipt claims open immediately. The durable opened_at/reason and
bff.refresh.circuit_opened metric are operational signals. Actual metrics export,
alert delivery and a private operator endpoint are not implemented in this core.

The coordinator derives X-Bff-Subject from the stable Identity session id using
the BFF MAC key and a separate purpose. An adapter shared by all requests no
longer carries one constant subject. Instances sharing BFF keys agree, client
credential rotation does not change the subject, and separate sessions have
separate opaque budgets. The client-wide cap continues to apply.

Six additional trials kill a peer at an independently selected random delay while
ten decisions race, without a stage probe. The parent waits for any in-flight
Identity bridge work to settle, then recovers in a separate process and checks
at most one rotation, zero predecessor reuse, and either committed-success
cleanup or terminal reconciliation of every unmatched marker. A terminal marker
is retained as audit evidence; it is resolved, not retryable. Trials cover a small
random sample, not exhaustive interleavings or replicated database failover.


## Upstream-first logout

IdentityRefreshHttp implements IIdentityLogout without redirects or automatic
retries. BffRefresh.Logout commits local LogoutRequested first, then holds the session row lock and reads current token material,
calls the bound Identity logout with that access proof and session, then erases
tokens and commits local Closed state. HTTP response loss/timeout/dependency
failure keeps locally unusable intent/material and returns BFF_UNAVAILABLE; another instance can
retry the same idempotent logout. A local terminal replay sends no HTTP request.
The existing Close method remains local-only cleanup and must not serve explicit
browser logout. Logout 403 preserves intent/tokens and contributes to the unverified distinct-session threshold.
A logout 401 closes the local user session; it does not certify upstream revocation
when the access proof is expired. The documented bounded Identity expiry remains.

RejectAccess accepts a trusted host's Identity Session-401 result and checks the
expected Identity session id under the row lock before local terminalization.
It feeds the same distinct-session rejection circuit; an open/triggered circuit
preserves the local state. This must never be called for BFF credential 403 or an
untrusted browser assertion. The core provides the methods; HTTP browser routes,
CSRF/Origin protection and operator/alert hosting remain pending.

Reset acceptance timing keeps its original whole-call floor/max and median/tail
spread bounds. After an observed 359.66 ms decoy sample exceeded the unchanged
350 ms bound, recovery-smoke adds wire time plus opt-in server diagnostics before
asserting the same bounds. Server-Timing includes reset work/total/deadline and
pipeline durations only when Development, Identity:ResetTimingDiagnostics=true,
loopback peer and the X-Reset-Timing-Key check against DevInboxKey all
hold. No unauthenticated/default request receives it. This diagnostic capability
is for isolated tests; it must not be enabled for public traffic because work
latency can disclose account-path differences. The earlier outlier cannot be
attributed retrospectively; new observations do not erase the failed run or prove
that CI tail variability is eliminated. There is no automatic test retry.

Current verified runtime `4edc2c3` passes 65 BFF checks within 263 C# checks,
296 emitted-event validations and all five API HTTP smokes on PostgreSQL 17.
The exact source, successful run and earlier failed candidates are recorded in
[VERIFICATION.md](VERIFICATION.md). A legitimate bulk session revocation can also
trip the rejection circuit; its threshold is an operational heuristic, not proof
of misconfiguration. Operators must inspect the cause before reset and calibrate
its behavior before real traffic.


## Recovery circuit and durable logout intent (supersedes latched-core behavior)

Migration 004 adds next_probe_at/probe_session and LogoutRequested. The refresh
circuit no longer depends on an operator reset to recover: after 60 seconds, a
conditional database update elects exactly one session for a recovery attempt
across all instances. The owner budget must remain below the 60-second probe interval. A validated successful elected refresh closes the circuit; failure
or process death leaves the next opportunity at least another minute away.
Normal in-flight successes cannot close another session's probe. Dispatch journal
reconciliation still precedes any send: permission never clears an unmatched
marker or retries a possibly consumed predecessor. A probe used for reconciliation
without an HTTP send can consume that minute's permit; this conservative delay
is preferable to replay. No permanent latch mode is enabled in this core.

A verified configuration NotProcessed receipt can open immediately. Unverified
403 responses, including WAF/proxy or malformed receipts, need three distinct
sessions in 30 seconds before opening the deployment circuit. One such response
quarantines only its session/marker. The existing five-session, 50-percent rule
for 400/401 remains. Non-403 unverifiable receipt claims still open immediately
as a receipt-integrity configuration signal. No unverified response authorizes
marker clearing or predecessor retry. Ordinary successful refresh records only
a per-session sample and reads committed circuit state; it does not lock the
singleton row. Only failures and elected recovery write that row. Samples make
the success denominator observational rather than an exact synchronized counter.
Controlled concurrency evidence is not a production load certification.

Explicit Logout first commits LogoutRequested while retaining sealed tokens.
GetAccess/RefreshIfDue now reject that cookie, even if Identity is unavailable.
A second transaction locks the intent row and calls idempotent Identity logout;
only a known completion (or access-proof 401 with the previously documented
revocation limitation) closes and erases material. Concurrent instances serialize
completion; lost responses leave durable intent for explicit retry. Refresh circuit
state does not block logout. Intent cannot revert to Active, and refresh cannot
rotate it. The browser host must expire its cookie immediately and schedule bounded
logout retries from retained server-side intent; autonomous retry worker/host and
retention are not implemented by the library. Unfinished intent can be safely
retried with the same cookie by trusted server code and has no ordinary access.

Operational investigation: inspect bff_refresh_circuit opened_at/reason and
next_probe_at plus the circuit metric; compare credential/receipt key configuration,
Identity health and proxy/WAF responses without logging secrets. For organic bulk
revocation, let the minute probe recover against a live eligible session; for
configuration failure, repair deployment configuration first. Never delete a
journal or force-retry its predecessor. Trusted Reset accelerates eligibility
for a fresh decision only; unmatched dispatch still requires reauthentication.
Metrics export and alert delivery remain release gates. Permanent lock policy
requires a separately delivered operator host, alert and accepted runbook.
