# BFF refresh core scaffold — 2026-10-10

The class library src/SmartCore.Identity.Bff implements the token-store/refresh-coordination slice of [BFF_REFRESH_DESIGN.md](BFF_REFRESH_DESIGN.md). It is called only by trusted server code. It does not expose HTTP/browser endpoints, serialize BffAccess to browsers, set browser cookies or implement CSRF/Origin routing. No production host or public onboarding is enabled. The opaque cookie value returned by Create is the future cookie secret, not a refresh token.

BffStore owns a separate schema/migration ledger (bff-database/001_bff_sessions.sql); it can use a separate database, and is not part of Identity's migration 001–011. Two application instances must use the same BFF database and BFF encryption/MAC keys, distinct from Identity's keys. The scaffold reuses the API project's database/crypto/token DTO dependencies; extraction of shared primitives is a future packaging choice, not a shared transaction or store. No new NuGet dependency is introduced.

Create seals the trusted login token bundle by BFF session/version, stores only a keyed opaque-cookie verifier and creates the fixed FK journal row in one transaction. GetAccess reads committed active access without row locking. It returns a RefreshDue signal for the host's bounded proactive scheduler; the library never starts detached background tasks. The host must call RefreshIfDue with its trusted activity classification around 80% of the issued access lifetime and for due foreground activity, even when cached access is still valid. Expired-access callers wait for a current successor; they cannot fall back to an expired token.

RefreshIfDue locks the session row, re-reads version/deadlines and journal, and uses the committed successor when another instance already satisfies the decision. A second connection commits the journal before exactly one Identity call. The owner stores the successor and attempt/version in its original transaction; cleanup uses attempt/version CAS. A crash, malformed/lost response, HTTP timeout or cancellation after dispatch leaves the durable marker. The current owner marks ReauthenticationRequired when it can commit within its reserved cleanup time; otherwise a later refresh owner recognizes exact committed success or irreversibly marks ReauthenticationRequired, erases tokens and never calls Identity with the abandoned predecessor. Already-valid cached access may still be used until that terminal state is committed or its remaining lifetime expires; this is the explicitly accepted bounded fast-path race, not permission to retry refresh. Full upstream logout orchestration is not delivered; Close only closes the local BFF cookie state and erases tokens.

The concrete IdentityRefreshHttp adapter owns a non-redirecting, non-retrying HttpClient, bounds response bytes and uses caller deadline cancellation. HTTPS is required; explicit development loopback HTTP is available for future native HTTP acceptance. The coordinator applies 5-second HTTP, 15-second owner (including a reserved 20% cleanup budget) and 17-second contender budgets; a PostgreSQL transaction cap covers wait plus owner time. Production transport credentials, signing/JWKS validation and hosting isolation remain release work.

BffActivity is a trusted server enum, never a raw browser assertion. Background keeps last_foreground_at unchanged; UserAction refreshes it only after successful successor commit. The browser host must classify allowlisted CSRF/Origin-protected actions and cannot map arbitrary headers, polling or a timer to UserAction. The token library cannot prove a human or enforce a route policy without that future host.

## Executable scope and limits

BffRefreshChecks is part of the native PostgreSQL C# harness. It uses two independent BffStore data sources/pools, actual Identity authentication/refresh transactions and a transport fault seam. It checks 20 parallel foreground decisions for exactly one rotation and no reuse, valid-access reads while rotation is paused, expired-access waiting longer than two seconds, five owner-loss stages, server-side PostgreSQL termination of the owning BFF connection, lost/malformed/timed-out responses, cancellation after dispatch, foreground after fresh background rotation, thirty-minute polling expiry, token erasure, definitive revocation excluded from uncertainty, and distinct-session uncertainty aggregation.

The transport seam calls the actual Identity service directly. Additional controlled loopback HTTP bridge checks exercise the concrete adapter: twenty parallel decisions on two instances send one authenticated HTTP refresh, an HTTP abort after the real Identity commit is never retried, and a redirect never forwards secrets. This bridge is not the complete Identity middleware pipeline. These tests do not prove browser cookie/CSRF endpoints, process-kill behavior, database failover durability, pool exhaustion or production load. Those remain the ordered acceptance work before adjacent browser features and real users. The core has no autonomous purge or production retention decision. Runtime 779c6ab passed native PostgreSQL CI with 25 BFF checks within 224 total C# checks, 230 emitted-event validations and five existing API HTTP smokes. Exact SHA/run/job evidence and limits are in [VERIFICATION.md](VERIFICATION.md).

## Admission rejection contract (authentication subset 0.7.0)

Identity issues a `NotProcessed` receipt only for authenticated refresh requests
rejected before invoking `AuthenticationService.Refresh`: `429 RATE_LIMITED`, or
reserved pre-invocation `503 AUTHENTICATION_BUSY`. Refresh presently performs no
KDF, so its current overload rejection is the admission/rate boundary. Dependency
`TEMPORARILY_UNAVAILABLE` is never a receipt. A fresh `X-Refresh-Request` UUID,
status/code pair and retry delay are bound with domain-separated HMAC-SHA256 using
the BFF client key. The adapter validates the bounded body and exact nonce. This
assumes a trusted Identity origin/TLS termination and confidential client key; it
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

Browser orchestration must invoke idempotent Identity logout before erasing the
access token locally. Identity 401 on a cached access resource call must close the
BFF session; neither upstream logout nor that resource-call handler exists in
this core-only slice. They must precede exposing browser routes.
