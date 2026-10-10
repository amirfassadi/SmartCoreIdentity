# Development authentication admission and worker hardening

Migration 006 preserves 001–005 and adds shared PostgreSQL caller windows and Person password-failure evidence. The development OpenAPI subset is now 0.3.0. Every authentication request requires a stable opaque `X-Bff-Subject` issued by the authenticated test BFF; it is admission evidence, not identity/authorization. Authenticate the BFF before allocating any source bucket. A real BFF must derive this assertion from trusted server-side context and must never forward a browser-selected value. Invalid client keys cannot allocate database buckets.

## Budgets and failure backoff

| Category | Per BFF subject / minute | Per authenticated BFF / minute |
|---|---|---|
| Login | 12 | 600 |
| Password change, including replay | 6 | 120 |
| Self, refresh and logout together | 120 | 6000 |

These are development presets, not measured production capacity. PostgreSQL upserts serialize each client's and subject's window across host instances. Client reservation precedes source allocation, so rotating subjects after client exhaustion cannot allocate unbounded partitions. The stored keys are purpose-bound HMACs of client/category/subject; contacts are not partition keys. Counters saturate, windows last one minute and request-driven bounded cleanup removes partitions older than two minutes after releasing admission locks. Dormant rows remain until traffic resumes, but allocation is capped by the finite client budgets. Session traffic no longer shares registration's 30/minute IP bucket. Registration retains that policy. A separate high 12000/minute process/IP transport ceiling bounds malformed/unauthenticated authentication traffic; it is not the end-user budget behind a BFF.

Login, current-password verification and new-password replay share one atomic failure row per known Person. Failures 1–3 do not delay; failure 4 delays one second, then 2, 4, 8, 16, capped at 30 seconds. Throttled presentations do not increment/extend the deadline or test the real password. Login uses dummy Argon2 work while throttled, preserving unknown-contact timing behavior; authenticated change/replay can reject before KDF because signed Person identity is already known. Verified passwords clear failure evidence; fifteen quiet minutes reset the history. No permanent status/fence is set by a guessed password. Atomic updates prevent parallel hosts losing increments. There can still be a bounded in-flight wave before failures commit; a four-slot process-wide KDF limit and distributed caller budgets bound its work. This does not claim to eliminate targeted temporary denial, and real BFF capacity/abuse behavior needs load tests.

Unknown contacts create no Person failure rows. LoginFailed records remain in the restricted outbox; admission bounds their creation rate but retention, aggregation, storage capacity and access/privacy policy are still pre-real-user work. Account backoff remains the same outward UNAUTHORIZED shape. Preverification throttling does not fabricate an InvalidPassword LoginFailed fact because the actual password was not checked; HTTP caller admission counters and the Person failure/deadline row retain bounded throttle evidence. Complete production security telemetry remains a release condition.

## Password replay and legacy migration

New change intents use request version 2. Request MAC covers only Person, Session, authenticated client and stable operation ID. It contains neither raw password. Exact new-password binding is checked using the stored replacement Argon2 encoding with VerifyPassword, outside every Session/Credential lock. Concurrent admitted-request recognition releases its Session transaction before this KDF. Original current-password proof is not retested on an admitted replay and its textual value is not part of replay equality. Replay requires the original still-valid signed Session/client proof and is itself subject to failure/caller budgets.

The replay encoding is immutable except erasure, retained for at most 900 seconds from admission and erased by the worker independently of the 24-hour encrypted recovery material. Recovering/reconciling Credential does not require keeping this replay verifier. MACs of expected/replacement Argon2 encodings remain outcome bindings; they are not raw-password oracles.

Migration 006 replaces legacy raw-password request MACs with independent opaque random bindings and updates linked receipts atomically. This is a one-time migration-role operation with the binding/receipt triggers disabled and re-enabled inside that migration transaction; operation identity, Credential evidence, terminal outcome and timestamps are preserved. Legacy request replay is rejected; legacy operations can still reconcile against the rewritten matching receipts. The migration replay test verifies no second rewrite. Existing backups/WAL can retain historical MACs; this migration does not claim retroactive erasure of those copies.

## Worker and operator

Both per-operation Tick handling and the BackgroundService loop catch every ordinary exception except cancellation. Known integrity/material rejection retains its explicit classification; any unexpected cast/null/cryptographic/dependency exception is OutcomeUnknown. Per-operation evidence and retry lease remain durable, and an outer scheduling failure emits a safe OutcomeUnknown/type log and continues the next loop. Passwords, request bodies, provider text and stacks are not logged. Cancellation remains cancellation. Native faults that terminate the process still require process supervision and durable lease recovery.

Operator audit now stores both requested action and effective outcome: Requeued, Applied or RejectedNotApplied. Historic rows are LegacyUnspecified; evidence is not invented for them. Resolving against an already Applied receipt records Applied even when ResolveNotApplied was requested. Repeating an action on a reconciled operation is also audited against its immutable receipt.

Password-change startup now requires Linux, explicit `Identity__ApplicationPort` and an absolute `Identity__AuthOperatorSocket` in a service-private directory with no group/other permissions. Kestrel binds the normal application to loopback TCP and adds that Unix socket. Operator routes reject **every TCP request**, including loopback and valid operator-key requests. The Unix listener rejects ordinary application routes. The operator key remains required on the private transport. A proxy pointing to the application TCP port cannot reach these routes; no X-Forwarded-* assertion enables them.

Never configure a reverse proxy upstream to the operator socket. A proxy must run as a different OS account from the Identity/operator service and have no access to the socket's 0700 directory; running the proxy as the same account defeats filesystem separation. Keep that rule in deployment review. No VPS/proxy configuration was changed in this slice, and no production isolation is claimed. The native HTTP smoke proves the network listener refuses even a valid operator key while the private socket accepts the separately authenticated operator.

## BFF interaction after change

Only after its own accepted change may a BFF briefly retry fresh login using the new password (for example, three attempts with bounded jittered delays around 250/500/1000 ms). Show a short pending message and stop after the budget; a generic 401 alone does not authorize indefinite retry or prove recovery is running. Respect caller limits and keep this distinct from refresh single-flight: zero-grace refresh predecessors must never be automatically retried after an uncertain result. Real BFF behavior is a remaining deliverable, not implemented by these backend changes.

Actual CI results are recorded in [verification](VERIFICATION.md). Reset, irreversible eligibility transitions, browser/BFF flows and production release remain separate work.
