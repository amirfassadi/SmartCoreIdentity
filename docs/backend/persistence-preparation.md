# PostgreSQL persistence implementation map

Status: candidate implementation map, not executable DDL. Baseline: [inventory](status-and-next-steps.md). ASP.NET Core/.NET 10 and PostgreSQL are provisional. Keep independent Identity and Credential commit boundaries even if databases are eventually cohosted. No cross-database foreign key or distributed transaction is assumed.

## Storage responsibilities

| Logical store | Required storage invariant | Required runtime evidence |
|---|---|---|
| Person | Unique ID; exactly one verified canonical email/mobile; unique contact across PendingCredential and Ready | Competing contacts commit one complete ownership triple; loser rolls back |
| Organization / Membership | Owner role only; durable Person/Organization references within ownership boundary; no cascade deletion on provisioning failure | Inject failures after each write; no partial ownership |
| Verification attempt | Absolute expiry, shared attempt budget, current code/binding HMAC, consumption version and replay mapping | Parallel wrong/right attempts, resend race, expiry, bound replay |
| Protected material reference | Opaque handle and durable owner/generation; transfer under same ownership commit | Stale cleanup cannot delete transferred material; no durable plaintext |
| Registration workflow | PendingCredential/Ready, OwnershipCommittedAt, immutable ReadyFactId/ReadyAt and winner binding | Concurrent completion has one Ready fact and one logical PersonRegistered |
| Identity Outbox | Stable EventId / unique logical operation, durable claim lease, attempts and next-attempt time | Commit then crash; lease expiry; duplicate delivery and idempotent consumer |
| Credential and winner ledger | One active Credential per Person; immutable registration winner; durable pre-Ready guard | Sequential and concurrent replacement both consult guard |
| Credential acknowledgment | Durable exact registration/ReadyFact/winner-generation receipt | Duplicate ack after later password change cannot restore old Credential |
| Session / refresh family | Fixed absolute expiry; version/current generation CAS; protected verifiers; revoked-family fact | Concurrent refresh: one rotation followed by reuse invalidation, no second usable family |
| Security event journal | Atomic with refresh mutation; payload restricted by candidate schema | Journal failure rolls back rotation/revocation; no token/verifier/hash leakage |
| Recovery/admin journal | Local effect and append-only audit in same commit, scoped expiring permit | Audit outage causes no mutation; expired authority cannot dispatch a new effect |

## Commit boundaries

A (Identity): guard proof consumption and canonical uniqueness, create ownership triple, transfer material reference, write PendingCredential/replay mapping and provisioning/ownership Outbox together.

B (Credential): commit initial active Credential, immutable winner, operation result and pre-Ready guard together. Any retry looks up durable operation identity; it does not provision another winner.

C (Identity): after authenticated current winner reconciliation, CAS PendingCredential to Ready; persist immutable Ready fact and enqueue PersonRegistered plus acknowledgment together. Ready event time differs from ownership commit time.

D (Credential): verify exact committed Ready fact and immutable winner/generation; persist acknowledgment and release guard together. Check matching historical acknowledgment before current Credential state on replay.

Session refresh: within Session's authoritative boundary, lock/CAS family state, classify current versus previously issued generation, rotate or revoke, and insert the internal event in the same commit. Reuse itself records family invalidation; do not require a fabricated public event. See [refresh contract](refresh-events.md).

## Migration acceptance checklist

1. Read actual backend mappings and active narrative attributes before writing DDL. Do not infer schema from archived examples alone.
2. Freeze supported PostgreSQL major, migration tool/version and immutable initial migration checksum; use an empty disposable database away from VPS.
3. Test migration from empty database, repeat application through migration history, and upgrade from the exact supported prior version.
4. Test actual unique constraints, proof/workflow/Session CAS, rollback injection, outbox leasing and transaction isolation with independent connections. Process locks are insufficient.
5. Exercise restore to an isolated database and reconcile Outboxes/winners after crashes. Rollback after real ownership data must preserve committed identities; never use cascading destructive teardown as recovery.
6. Separate migrator privileges from application privileges; secrets come from external configuration, never source control. Do not claim least privilege until grants and application access are tested.
7. Retention jobs stay disabled until explicit finite policy is adopted. Do not prune immutable winner bindings needed throughout registration lifetime or delete live/transferred material.

No database instance, schema name, actual password, production connection string or server topology is supplied by this document.
