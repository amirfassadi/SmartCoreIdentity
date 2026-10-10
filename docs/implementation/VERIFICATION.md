# Registration phase 1 verification — 2026-10-09

## Executed locally

- .NET SDK 10.0.401; build succeeds with 0 warnings and 0 errors (warnings treated as errors).
- PostgreSQL engine used here: **PGlite 0.5.8 with pglite-socket 0.2.11**. Native PostgreSQL cannot start in this executor's user environment. PGlite is preliminary integration evidence; its connection multiplexing is not native PostgreSQL concurrency proof.
- Executable C# integration harness: **21 checks passed**. Covers invalid input, no ownership before proof, bound idempotency, wrong-binding attempt persistence, atomic ownership, lost-response replay, protected-material transfer, transactional outbox, immutable initial winner, separate Ready time/event, durable acknowledgment, phase-by-phase restart reconciliation, duplicate verified contact, mobile-only flow, attempt exhaustion, resend expiry, proof/material cleanup, injected PostgreSQL trigger failure/rollback, retry after rollback, and concurrent confirmation calls under the test engine.
- HTTP smoke passed against the actual ASP.NET process: 202 initiation, 201 PendingCredential, 200 authorized Ready replay, no token issuance, no-store header, authenticated loopback-only fake inbox, rejection of unknown/null fields, password/header validation, and absence of public registration lookup.
- **12 emitted domain events** validated against the pinned Platform JSON Schema, including UUID actor attribution for ownership creation and System attribution for worker-produced Ready. HTTP responses also validate against the implemented OpenAPI schemas.
- Fixed a real replay defect found by tests: application timestamps now use PostgreSQL microsecond precision before first response, preserving exact timestamp equality on replay.

The harness simulates interruption by stopping between committed stages and resuming canonical operations. It does **not** claim OS-kill coverage, independent-service network partitions, restore reconciliation, or all ADR-0004 failure scenarios.

## Repeatable native gate

`.github/workflows/backend.yml` starts PostgreSQL 17 and runs the same harness on push/PR. A configured workflow is not a PASS; record the actual run separately after execution. Docker Compose/image startup, host deployment, provider integration, full Session/reset/recovery and T16 consumer tests are not claimed by the local results.

`contracts/events.input.schema.json` is a byte-preserved test input from Platform `240151e25c5d9f67a58a6f4eea729a667934cc2b`; it is not a new approval of every event. The implemented public subset is `contracts/registration.openapi.yaml`. The broader upstream proposals and archived uploaded Blueprint are preserved separately.

Public deployment remains disabled. See [baseline](BASELINE.md) for exact remaining capabilities and review gates.
