# Backend continuation — 2026-10-10

Status: reviewed remote inventory; preparation candidate, not release approval.

## Reproducible baseline

- Identity main: f72a4fdf9f29edd3970b4bd719bfbf8040089534. Three documentation files; no executable backend, database migrations or runtime tests.
- Identity reconciliation: f0781c1415b3a784788c326b93c2cd45bd5206cd. Archived Blueprint, registration review documents and documentary checker.
- Platform review/slice0-evidence: 240151e25c5d9f67a58a6f4eea729a667934cc2b. Proposed integrated contracts and partial validation evidence.
- No remote implementation/registration-backend branch was advertised during this review.
- Previous conversation reports mention local implementation/registration-backend and 4014b9c, but conflict about whether executable code exists. Neither that commit nor the reported 21 runtime checks was reproduced from GitHub. Do not report them as current PASS.

Owner instruction for this work: continue database/backend/document preparation from GitHub; leave existing local code and VPS untouched. New working copy is isolated. .NET 10 / ASP.NET Core and PostgreSQL remain the provisional engineering baseline, not a final stack decision. Database major version and co-location are not selected here.

## Ordered work and evidence

| Step | Deliverable | Completion evidence / dependency |
|---|---|---|
| 1 | Remote source inventory and isolated review branch | Exact SHAs above; original code and host untouched |
| 2 | Safe internal refresh event contract and transaction map | [Refresh contract](refresh-events.md), strict candidate JSON Schema and documentary checks |
| 3 | Database implementation map | [Persistence preparation](persistence-preparation.md); no deployed migration implied |
| 4 | Recover authoritative backend source | Owner publishes existing local branch or provides its source archive; resolve contradictory prior reports before creating a second backend |
| 5 | Reconcile active Platform/Identity contracts | Record latest owner-selected policies, formal source dispositions and outstanding 064/065 gates; do not mark old open forms accepted automatically |
| 6 | Implement verification + ownership | Shared attempt limits, proof CAS, unique canonical contact, ownership triple and Outbox in transaction A; real database rollback and concurrency evidence |
| 7 | Implement Credential + Ready recovery | Separate B/C/D commits, immutable winner, guard/ack protocol, lost-response and cleanup race tests |
| 8 | Implement login, self, refresh, logout | Ready gate; refresh generation CAS/reuse revocation; explicit Session expiry and idle policies; authenticated self read |
| 9 | Complete MVP security and profile | Password reset before real-user registration; password change closes all Sessions; profile updates and T16 consumer tests |
| 10 | Build/release evidence | Identified SDK/package locks, real PostgreSQL migrations and rollback/restore, runtime security tests; no documentary count substitutes |
| 11 | VPS and domain rollout | After owner returns: domain/DNS, fresh host inventory, selected database instance, backups/resource headroom, secrets/provider setup and controlled deployment |

Steps 1–3 are preparation. Steps 4–10 do not require a purchased domain. A domain does not close code-entry, policy, provider, database or release gates. VPS work is explicitly paused by the owner.

## Actual owner dependencies

Immediate: make the existing local backend branch available on GitHub (publishing it does not require changing its code), or provide an archive of that branch. Remote documentation can continue independently, but executable continuation needs authoritative source.

Before production retention/cleanup: choose finite retention for internal refresh events and security audit, including replay horizon and purge procedure. No retention number or automatic deletion is invented.

Before deployment: domain name, fresh host inventory and intended database instance. Concrete host data remains private; public repository contains no server addresses, account names or credentials.

## Verification limits

The existing registration checker verifies archived file hashes and local Markdown links. New preparation checker validates this candidate event schema and examples; neither runs the backend or certifies complete Blueprint validation. This change does not merge main, amend accepted ADR-0004, select co-location, or apply SQL to any database.
