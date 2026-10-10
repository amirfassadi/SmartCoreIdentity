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

## Recovered baseline and setup revision — 2026-10-10

The recovered baseline was published unchanged in GitHub commit b4e1407 (source 770486d). [Native PostgreSQL 17 CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38049944311) completed successfully, including the original C# harness, event checks and HTTP smoke. This is new native evidence for that baseline, not for changes after it.

Setup/completion revision local evidence: .NET SDK 10.0.401, Release and Debug builds with zero warnings/errors; **37 C# checks passed** against PGlite 0.5.8 / pglite-socket 0.2.11. **33 emitted events** passed the pinned Platform schema. Existing registration HTTP smoke and new setup HTTP smoke passed with OpenAPI response validation; OpenAPI 0.2.0 passed the spec validator.

New cases cover separate setup-code purpose, protected binding, challenge replay without delivery/expiry extension, existing automatic winner, automatic/setup concurrency, identical completion concurrency, exhausted attempts, changed candidate replay, expired proof disposal, injected Credential transaction failure and worker completion of accepted material after proof expiry. HTTP cases explicitly reject null/unknown fields and missing idempotency headers. Runtime rejection of malformed input is verified, separately from resolving the compiler's nullable-input error.

The first native PostgreSQL 17 run for this revision [failed](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38050872751) in the automatic/setup concurrency case with SQLSTATE 40P01. Setup completion held a challenge row before taking its registration foreign-key lock, while provisioning held the registration row before clearing challenge material. Completion now acquires the same per-registration Credential advisory guard before locking its challenge. This preserves separate acceptance and Credential transactions while imposing one lock order. The existing automatic/setup race is the regression case; a successful native rerun must be recorded before claiming this revision passes. PGlite multiplexing remains preliminary concurrency evidence.

Formal administrative recovery, OS-kill/network partition/restore tests, provider delivery, login/Session/password reset and production release remain outside this revision's PASS claims.

### Native regression result

GitHub commit `c49e3ee776565c2e2e9adcae2e47ae417c67cf8e` includes the lock-order fix. [PR-triggered PostgreSQL 17 run](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38051247469) completed successfully: **37 C# checks**, **33 emitted events** validated against the pinned Platform schema, original HTTP smoke and setup HTTP smoke, and zero-warning/error builds. The automatic/setup and identical-completion concurrency cases both passed under native PostgreSQL. Documentary candidate checks also passed on this commit. This closes the observed 40P01 regression; it does not broaden the failure/concurrency coverage beyond the executable cases.

## Owner-selected A: authentication storage/gate evidence — 2026-10-10

At Amir's explicit design disposition, A retains separate Credential commits; B requires evidence and a separate ADR. Storage code commit `f0969b7f36bc57d924e986b0dfed3589981b3455` adds migration 003 and `IAuthenticationIssuanceGate`, with no new HTTP authentication/reset route. [Native PostgreSQL 17 PR run](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38052874393) completed successfully: **54 C# checks** (37 registration/setup + 17 storage/gate), **33 public event fixtures**, both existing HTTP smokes, locked dependency restore and zero-warning/error builds. Documentary candidate checks also passed.

New executable cases cover migration replay, gate initialization/rollback, competing Current generations, family/Session deadline binding, wrong-Session internal event attribution, immutable absolute and recognition deadlines, consumed predecessor finality, monotonic family generation and Person epoch, persistent blocked state, unresolved-fence release rejection, terminal closure, reconciled-epoch matching and concurrent gate writers advancing distinct epochs without a lost update.

Mutation admission and reconciliation in these tests use **internal preverified fixtures**, not real password/reset proof or actual Credential replacement. Native gate serialization is evidence for this storage adapter; it does not certify cross-store authentication protocol or token issuance. Login/refresh/logout/self endpoints, actual proof-gated mutation, scheduled password reconciliation, restricted operator status, BFF integration, key rotation and sensitive online endpoint enforcement remain future runtime work. Zero replay grace is recorded policy; token-reuse enforcement is not yet an implemented endpoint. General access still has the selected bounded residual lifetime.
