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

## Development authentication and evidence immutability — 2026-10-10

Runtime commit `425fdc30b7209e609659bc0b6d72fccc8fb4eb85` implements opt-in authenticated test-BFF login/self/refresh/logout and forward migration 004. [PostgreSQL 17 PR run](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38054388050) passed **74 C# checks** (37 registration/setup, 21 storage/gate, 16 authentication), **59 emitted public event fixtures**, locked restore, zero-warning/error builds, documentary checks and all three real HTTP smokes. Unlike the prior storage-only evidence, this revision verifies endpoint rotation/reuse enforcement and online self eligibility. Added closure/consumption-field mutation regressions passed.

Authentication cases include Pending denial, uniform unknown/wrong-password errors, bounded signed access, invalid signature/signing key/client, wrong-client and forged-refresh nonrevocation, zero-grace consumed replay, measured reuse latency, concurrent same-generation reuse, logout replay, exact access/idle/absolute expiry, foreground/background idle behavior and epoch/fence eligibility. Internal mutation fixtures still do not replace Credential, activate a public reset or perform actual scheduled/operator recovery. Those paths and production BFF remain unimplemented, with public onboarding disabled.

The first local authentication run found a fixture dependency on pre-existing Pending records; the fixture now creates its own verified Pending registration. The local event checker also caught an extra SessionReference on Session events; only LoginSucceeded's closed schema admits that field. It was corrected before the successful native run above. No failing fixture/event run is counted as PASS.

Two follow-up executable cases add logout/refresh concurrency and access-deadline clipping close to Session expiry. Commit `ae40de8db83c6eee0fadf791dd679b770bf6126b` passed the [final PostgreSQL 17 PR run](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38054593904): **76 checks** (37 registration/setup, 21 storage/gate, 18 authentication), **62 public event fixtures**, all three HTTP smokes, locked restore, zero-warning/error builds and documentary checks. Runtime code is unchanged from the earlier successful authentication revision. Local PGlite also passed 76 checks and 62 event fixtures; it remains preliminary concurrency evidence. This final documentation-only update records those outcomes.

## Latest documentation head independently verified

Commit `37ed79c709a0a9eb5c594996dfac6e89405e5d34` changed only VERIFICATION.md. Its own [PostgreSQL 17 PR run](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38054833309) completed successfully. Job `114221038471` logs explicitly contain `RESULT: 76 checks passed. Backend: PostgreSQL 17`, `PASS: 62 emitted events match pinned Platform schema`, and original registration, setup and authentication HTTP PASS lines. Locked restore and both builds passed; [documentary run](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38054833337) also succeeded. This independently checks the documentation head rather than assuming the earlier ae40de8 run applies.

## Password-change recovery candidate

Commit `0a451bedfa1a417a6a84bc0a52a0bf7bec028b47` implements migration 005, authenticated development password change, the independent Credential receipt adapter, scheduled reconciliation and the restricted development operator adapter. Its [PostgreSQL 17 PR run](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38058478681) passed **94 checks** (the prior 76 plus 18 actual password-change/recovery checks), **92 emitted events** validated against the pinned Platform schema, locked restore, zero-warning/error Release and Debug builds and all **four HTTP smokes** (registration, setup, authentication, password change). [Documentary checks](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38058478817) also passed. Job `114231705908` logs were read directly; these counts are not inferred from configured tests.

New cases exercise invalid policy/proof without fencing, identical concurrent admissions, all-Session revocation, a deterministically paused old-password login crossing actual replacement/reconciliation, restart from admitted state, lost Credential result and receipt replay after later changes, immutable receipt, an injected Credential transaction failure at attempt eight, authenticated operator redrive, 24-hour material expiry, terminal not-applied reconciliation with a stale-worker retry, honoring an Applied receipt during operator resolution, transactional operator audit and a real password-change/refresh race. The HTTP password smoke checks BFF versus operator-key isolation and malformed/null/unknown proof input, policy before Session closure, a real scheduled worker, replacement login, old Session/refresh denial and exact accepted-request replay.

These are durable-boundary simulations using real transactions and newly instantiated coordinators, not OS-kill, network partition, restore or a production authorization audit. Local SDK/database runtime are unavailable in this resumed environment; Python syntax, YAML parsing and new relative links were checked locally, while native CI supplied compilation/runtime evidence. Authentication rate-budget and eligibility-transition requirements remain documented conditions, not implemented production controls. Reset proof/delivery/completion and reassigned-number recovery remain absent; real-user onboarding remains disabled.

### Access-expiry admission regression

Runtime commit `da2031a4cb494205078fddfdfcbe5e6b73fd3dcc` additionally revalidates access JWT lifetime under the issuance gate after KDF/lock wait. A deterministic paused-admission test advances the clock to access expiry and proves no intent or epoch advance occurs. Its own [PostgreSQL 17 PR run](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38058787590) passed **95 checks**, **94 emitted events**, all four HTTP smokes, locked restore and zero-warning/error builds; [documentary checks](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38058787566) passed. Job `114232607657` logs were inspected directly. The prior evidence-only head `71b8f0378b98fa5c8c0ab710e7aad99669fe7b2d` also passed its own [94-check PR run](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38058672166). This subsequent update changes only this verification record; runtime remains exactly the verified da2031a4 revision.

## Authentication admission / worker-hardening candidate

Runtime commit `e987808a10915992b001a246bd75225209d6dab0` adds migration 006 and the accompanying worker, password replay, audit, Unix-socket isolation and distributed admission changes. Its [native PostgreSQL 17 PR run](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38060548011) completed successfully: **113 C# checks**, **108 emitted events** validated against the pinned Platform schema, locked restore, zero-warning/error builds and all **four HTTP smokes**. Job `114237753236` logs were read directly. [Documentary checks](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38060547978) also passed.

The new checks cover shared Person failure/backoff behavior across login and password change, parallel accounting and caller budgets, partition allocation/cleanup, unexpected Process/Tick exceptions, replay-verifier erasure, effective operator outcome, and one-time legacy migration. HTTP smokes require the trusted BFF subject and exercise more than 30 same-IP self reads plus refusal of a valid operator key on the application network listener. Local Python syntax/YAML/link checks are preliminary; native CI supplied compilation and PostgreSQL runtime evidence. This subsequent evidence update changes only this verification record.

These remain development controls and durable-boundary simulations. Production BFF integration, refresh single-flight across instances, load acceptance, failure-event retention/aggregation and named operator authorization are not certified by these checks. Deployment must exclude the private operator Unix socket from proxy routing and isolate the proxy OS identity; no deployment/proxy configuration was changed. Eligibility-transition mutation and public password reset remain absent, and real-user onboarding remains disabled. See [authentication admission](AUTH_ADMISSION.md) for budgets, bounded backoff, replay lifetime and remaining release conditions.

## Sealed replay and bounded KDF queue candidate

Migration 007 removes the prior clear Session-side replay encoding while preserving independently sealed Credential recovery material and receipts. Newly admitted changes seal their bounded replay verifier with an operation-specific AES-GCM purpose. Password work retains four running slots and now allows sixteen queued waiters for at most two seconds, with 503 AUTHENTICATION_BUSY separate from 429 RATE_LIMITED. New checks exercise purpose isolation, storage immutability, clear-verifier upgrade, burst draining, bounded queue overflow/timeout, cancellation and slot release after failure. Runtime commit `f18b6c93c124561386e66702fa7238aa4eb1ad58` passed [native PostgreSQL 17 PR CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38061859964): **123 C# checks**, **108 emitted events** against the pinned Platform schema, all four HTTP smokes, locked restore and zero-warning/error builds. Job `114241575863` logs were inspected directly, including the ten new sealed-material/migration/queue checks. [Documentary CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38061859971) also passed. This follow-up only records evidence and updates explanatory docs; runtime remains that verified revision. These are functional checks, not a production load test or a reset authorization decision. The proposed reset proof policy is recorded in [reset decision](RESET_PROOF_DECISION.md) and is not selected automatically.

## Recovery enrollment and initiation candidate

Migration 008 adds opt-in code enrollment/replacement and reset initiation only. New checks exercise 128-bit one-time presentation, lost-response receipt replay, Person/version MAC binding, immutable receipts and version updates, email/mobile policy, atomic account-notification tasks, BFF enrollment state, purpose-separated OTP material, uniform known/unknown initiation, fixed proof expiry, shared Person delivery budget, no initiation fencing, and expiry/password-change races. The new HTTP smoke validates strict authenticated enrollment and the initiation subset, including protected inbox and an absent completion route. Runtime commit `a5f338ca1dce11ede4e39ece1856d51f5a323a98` passed [native PostgreSQL 17 CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38071485737): **143 C# checks**, **127 emitted events** against the unchanged pinned Platform schema, all **five HTTP smokes**, locked restore and zero-warning/error builds. Job `114269619197` logs were inspected directly. [Documentary CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38071485727) also passed. The proposed event schema check separately accepted existing Session context and sessionless recovery context while rejecting missing, ambiguous and secret-bearing contexts; this is proposal conformance, not actual reset completion. Local Python syntax, YAML/security references and new relative links passed, but local jsonschema/SDK runtime are unavailable. This follow-up changes only this verification record; runtime remains the verified revision. The proposed event schema is tested separately in CI and is not a public contract approval or reset runtime PASS.

## Dual-factor reset candidate, 2026-10-10

Platform schema 1.2.2 is pinned from `49ea40a50854c447b18afbdd7c2f3116dbee3136` with source provenance. Migration 009 adds deletable decoys, shared Person proof limits, immutable reset acceptance and receipt-backed recovery-code reservation/consumption. The worker and private recovery operator are reused; no synthetic Session is created. See [PASSWORD_RESET.md](PASSWORD_RESET.md). Initial runtime commit `5110208abacec717e074e39b46d1174e9ff3388a` passed [native PostgreSQL 17 CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38075470199): **161 C# checks**, **155 emitted events** against the selected Platform 1.2.2 schema, all **five HTTP smokes**, locked restore and zero-warning/error builds. Job `114281412555` logs were inspected directly. Documentary run `38075470193` also succeeded. Counts above apply to that exact runtime commit; the final follow-up evidence is recorded below.

The small HTTP timing sample on that commit observed known-contact median 12.23 ms (2 samples), unknown median 2.81 ms (3), and suppressed median 3.74 ms (3). The small sample is not a benchmark, but database-path timing differs; response-shape equality is not account-enumeration resistance. Before real-user release, mitigation and repeated representative timing/adversarial acceptance testing are required. Platform PR 11 has no configured native workflow run on its source SHA; its imported schema is exercised by Identity CI.

Final runtime commit `b51c6b9155ac6fa016d5fee67dce8c2bec4a0306` passed [native PostgreSQL 17 CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38075799202): **168 C# checks**, **171 emitted events** validated against Platform schema 1.2.2, all **five HTTP smokes**, locked restore and zero-warning/error builds. Job `114282375758` logs were inspected directly, including reset/password-change competition, proof expiry during gate wait, competing proof use, FailedClosed preservation/operator recovery, premature-consumption refusal and telephone OTP-only rejection. [Documentary CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38075799225) also succeeded. Exact Platform/Identity schema byte equality was checked through the source connector at the pinned SHA. Local Git tree `c276a36935c84925879e4c496175eea6e6545ce2` equals the published runtime tree; commit metadata differs between the recovered local checkout and GitHub.

The final small timing sample observed known median 5.85 ms (2), unknown median 3.59 ms (3) and suppressed median 4.31 ms (3); maxima were 5.92, 17.48 and 4.40 ms respectively. Both observations are retained to show variability and the small sample limit. They do not establish timing-based enumeration resistance. This follow-up changes documentation only; runtime remains the verified revision above. Neither these functional checks nor queued notifications certify public release, provider delivery or production load capacity.

## Reset timing and abuse-control candidate, 2026-10-10

Migration 010 / OpenAPI 0.6.0 add eight-digit fresh reset OTP, escalating Person cooldown and bounded proof-limit alerts. Both reset endpoints use a common response deadline after disposing locks/connections; service/HTTP timing acceptance covers real/decoy/suppressed branches, with privacy-safe overrun telemetry and explicit load limits. Recovery-code ASCII case and surrounding whitespace are normalized by the API before verifier/receipt binding; passwords are never normalized. Platform merge precedes Identity merge/release. See [PASSWORD_RESET.md](PASSWORD_RESET.md). Runtime commit `16ca5dc8f9074ee92752269397618f41bd51a9f3` passed its own native CI; exact evidence follows. Previous counts refer to recorded revisions.

Runtime `16ca5dc8f9074ee92752269397618f41bd51a9f3` passed [PostgreSQL 17 CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38077120454): **195 C# checks**, **188 emitted events** against the unchanged pinned Platform 1.2.2 schema, all **five HTTP smokes**, locked restore and zero-warning/error builds. Job `114286313301` logs were inspected directly. [Documentary CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38077120442) also passed. Local tree `97dfe7142bd125f04bd5bd6f4c731752e3f3a4b3` matches the published runtime tree. New checks cover escalating 15/30/60/120/240/360-minute cooldown, six-hour cap, quiet-period reset, atomic concurrent cap crossing, once-per-crossing alerts, lowercase/whitespace proof and replay normalization, fresh eight-digit OTP, and privacy-safe failed-response overrun instrumentation.

Both reset service paths were measured against real PostgreSQL with 12 samples per real/decoy/suppressed group. Fresh eligible initiation medians were 210.45/205.56/210.34 ms; invalid completion medians 210.58/209.41/208.03 ms. HTTP acceptance additionally checked both routes (12 samples/group each):

| HTTP path | Real p50 / p90 / max (ms) | Decoy p50 / p90 / max (ms) | Suppressed p50 / p90 / max (ms) |
| --- | --- | --- | --- |
| Completion, invalid proof | 212.48 / 222.07 / 250.77 | 209.49 / 219.36 / 238.12 | 210.46 / 226.66 / 237.46 |
| Initiation, bound real replay / fresh other paths | 209.75 / 221.85 / 309.30 | 213.15 / 222.14 / 222.41 | 216.70 / 228.91 / 312.45 |

All measured groups passed the declared floor, maximum and median/tail-spread thresholds. Transport/scheduling maxima exceed the chosen service deadline in some samples; the table preserves those outliers. Twelve samples do not establish statistical indistinguishability or adversarial-load acceptance. A contended/database-slow request can exceed its deadline and expose its processing path; the overrun instrument must be exported and failure overruns monitored before real-user release. Stronger cooldown also increases the accepted denial-of-recovery window when an attacker knows the contact. Alerts here are atomically queued tasks, not delivered messages. This follow-up changes documentation only; the verified runtime is the SHA above. Platform merge must precede Identity merge/release.

## One-hour cooldown follow-up — verified runtime d9b50e7

Migration 011 and the matching runtime cap shorten reset stops to 15/30/60 minutes; tests cover repeated capped windows, upgrading an existing six-hour stop without extending shorter stops, migration replay, and separate success/failure overrun tags. Runtime `d9b50e7db4c4edcf1e90e3fbdb596b4ad7ed3673` passed [native PostgreSQL 17 CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38078342442): **197 C# checks**, **188 emitted-event schema checks**, all **five HTTP smokes**, both reset timing gates, locked restore and successful zero-warning/error builds. Job `114289911400` logs were inspected directly. [Documentary CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38078342435) passed as well. Local/runtime tree is `ba711819f403779085e304cfe04c53d4c49a2041`. Earlier 195/188 counts describe the preceding revision. The current follow-up updates documentation only.

Controlled 12-sample HTTP completion groups real/decoy/suppressed had p50 217.22/210.48/210.52 ms and p90 221.34/222.02/221.03 ms. HTTP initiation real replay/fresh decoy/fresh suppressed had p50 211.80/212.75/209.30 ms and p90 218.56/221.93/222.20 ms. All groups passed the same thresholds; these small samples do not substitute for representative load acceptance. Failure-only overrun monitoring and actual notification delivery remain release requirements. The owner selected retaining Active-only Person MVP scope; future eligibility writers still require the documented epoch/revocation protocol before introduction.

## Active-only constraint and BFF design follow-up — verified f0e3144

The harness now checks PostgreSQL's actual validated Person and Credential status CHECK predicates for exact Active-only scope. The BFF_REFRESH_DESIGN.md document specifies server-side tokens, shared-store row serialization, independently committed dispatch evidence before Identity HTTP, successor commit recognition and BFF-controlled user-action foreground routing. This is design, not implemented BFF or multi-instance acceptance. Runtime/check revision `f0e31440694ca867d86da302bef0bcbcb4d9d46b` passed [native PostgreSQL 17 CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38079795586): **199 C# checks**, **188 emitted-event schema checks**, all **five HTTP smokes**, both reset timing gates and zero-warning/error builds. Job `114294168730` logs were inspected directly, including the two actual validated status CHECK assertions. [Documentary CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38079795582) passed. Local/source tree is `ad65354d8a07d4d66a12b76c7a40057dc1c906c5`. Earlier 197/188 evidence remains revision-specific. This evidence follow-up changes documentation only.

The completion HTTP real/decoy/suppressed p50 values were 213.66/211.25/216.06 ms, p90 220.31/279.22/219.45 ms, and maxima 223.00/303.71/221.94 ms. The p90 spread of 59.77 ms passed the declared <60 ms bound but is close to it. Preserve this observation: no threshold was widened and no test retried to hide it. The small sample cannot attribute the tail to execution, scheduling or transport, establish statistical channel evidence, or prove reliable thresholds under representative load. Initiation p50 values were 212.74/208.26/208.65 ms and p90 221.88/217.81/217.76 ms. The 12/group gate remains regression evidence only; investigate future flakes and require adversarial/load testing with failure-overrun monitoring before real-user release.

## BFF core scaffold and behavioral Active-only checks — verified 779c6ab

Person/Credential status regression now attempts inactive writes inside rolled-back transactions and requires PostgreSQL 23514, without depending on generated constraint names or pg_get_expr formatting. The new BFF class-library scaffold and BffRefreshChecks cover shared-store coordination, native Identity rotation, owner/transport faults and foreground. The test uses two independent data sources/pools, not two deployed browser hosts; controlled HTTP bridge tests also exercise the concrete adapter, real response loss and redirect rejection; complete middleware, production failover/load/browser acceptance remain outstanding. Runtime `779c6ab7d2b15d149b6adfb37b2d714e3691a765` passed [native PostgreSQL 17 CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38081091659): **224 C# checks**, **230 emitted-event schema checks**, all **five existing API HTTP smoke scripts**, both reset timing gates and zero-warning/error builds. The 26 new BFF checks, including the controlled loopback HTTP bridge, are part of the C# count; they are not a sixth browser smoke suite. Job `114297980653` logs were inspected directly. [Documentary CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38081091647) also passed. Local/runtime tree is `db4c7d15e1c3f55d90123ab2fb9102c42c629f07`. Earlier 199/188 results remain revision-specific. This follow-up changes documentation only.

Directly observed PASS: 20 foreground contenders on two independent BFF stores/pools caused one Identity rotation; valid committed access was read during a held refresh lock; an expired-access contender waited past two seconds and reused the committed successor. Fault coverage includes before dispatch, durable dispatch before HTTP, response received before successor write/commit, committed successor before marker cleanup, actual server-side PostgreSQL owner connection termination, injected lost/malformed/timed-out responses and cancellation. Separate loopback HTTP checks observed one authenticated refresh for 20 contenders, an actual connection abort after Identity commit without replay, and a redirect with zero trap calls. Background rotation did not advance foreground time, user action after a fresh background rotation coalesced once, and polling could not pass the thirty-minute boundary. Known revocation was excluded from uncertainty and terminal local cookie closure erased token material.

The first core candidate `92ca8a9` passed compilation and the initial concurrency/fault checks but failed the exact polling-deadline comparison because the test clock retained 100-nanosecond precision while PostgreSQL stored microseconds. The final source normalizes BFF timestamps with the existing Identity Timestamps.Now helper and compares the same precision; the complete rerun passed. No timing threshold was widened or retry used to hide that failure. Latest HTTP reset completion p90 real/decoy/suppressed: 220.34/221.27/220.57 ms; initiation: 223.26/220.16/222.98 ms. These remain small regression samples, not statistical/load certification.

This is a class-library token coordination scaffold, not a deployed browser BFF. The two coordinators share a PostgreSQL store through independent pools in one test process, and the controlled HTTP bridge calls real Identity services without the complete middleware pipeline. Separate-process kill/DB failover durability, pool/load acceptance, browser cookies/CSRF/Origin and real logout/onboarding/provider integration remain release dependencies. The initial uncertainty thresholds are review triggers, not a change to zero-grace policy.


## BFF admission receipts and actual OS process death — verified 9a9da68 (recorded 2026-10-11)

Runtime `9a9da68b203036c2beaf8bb37cccc5c1c5616a01`, source tree
`55ee7a3cad35ccb94654c486ce171f162fbe64b7`, passed
[native PostgreSQL 17 CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38082500995):
**242 C# checks (44 BFF checks)**, **256 emitted-event schema checks**, all
**five existing API HTTP smoke scripts**, both reset timing gates, locked restore
and zero-warning/error builds. Job `114302131842` logs were inspected directly.
[Documentary CI](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38082501002)
also passed. This evidence follow-up modifies documentation only.

Observed admission PASS: signed nonce-bound `NotProcessed` rate/busy receipts
CAS-clear only their dispatch attempt, retain the active token bundle/version
and cause `BFF_BUSY` with a bounded retry delay. Twenty cross-instance contenders
within the shared retry deadline sent no additional HTTP dispatch; later attempts
rotated the original unconsumed token once. Generic proxy 502/503/504, wrong nonce
and invalid proof remained uncertain without retry. The real Identity middleware
HTTP smoke exhausted a subject's session budget, checked the 429 receipt MAC and
rotated that same token with a fresh admitted subject. The 503 busy receipt is a
controlled bridge case: current Identity refresh has no KDF, and the contract
reserves this receipt for pre-invocation admission only.

Observed process PASS: twenty refresh decisions from two independently launched
OS processes/pools/HTTP clients made exactly one real Identity HTTP rotation.
The parent forcibly killed a peer paused at each of BeforeDispatch,
DispatchCommitted, ResponseReceived, SuccessorWritten and SuccessorCommitted.
Independent recovery safely made the first dispatch when no marker existed,
recognized the exact committed successor after commit, and never resent the
predecessor for the three unmatched-marker boundaries. These are actual process
kill/connection-release tests, distinct from the retained exception seams and
PostgreSQL connection termination. The PostgreSQL 17 startup guard passed;
version-16 rejection uses a supplied Version value, not an actual PostgreSQL 16
server deployment.

The first candidate `86ea1d2cdf4975187b3814d37a0a333585e335ff`
([run 38082252626](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38082252626))
passed compilation, admission checks and separate-process contention but failed
normal peer teardown: DONE reached the parent before clean process exit, and
cleanup incorrectly required an abnormal kill exit. Cleanup now accepts normal
completion; explicit kill of a paused fault peer still requires abnormal process
termination. The complete revised run passed. No timing threshold was widened
or test retried to conceal the failure.

Counting correction: the 779c6ab job `114297980653` has **26** BFF PASS lines,
previously reported as 25. Its total of 224 C# checks remains correct. The
historical BFF count in this file and BFF_CORE.md is corrected; the current count
is 44, with 18 added executable checks.

Latest controlled HTTP reset completion p90 real/decoy/suppressed:
222.05/221.57/222.43 ms; initiation: 220.59/218.17/219.77 ms. Twelve samples per
group remain regression evidence, not statistical channel or load certification.
A single PostgreSQL server and controlled Identity bridge do not prove replicated
failover durability, browser cookies/CSRF/Origin, production pool/load behavior,
upstream logout/resource-401 handling, delivery providers or real onboarding.
The browser host must call Identity logout before token erasure and terminalize
its session on Identity 401. Those browser handlers remain unimplemented.
Platform merge/canonical pin verification must precede Identity merge/release;
both PRs remain drafts.


## BFF configuration protection, upstream logout and random process kills — 2026-10-11

Verified runtime source: `4edc2c392a4413b72865e2c9361fa507721133e1`, tree
`6c6a7affa5dc5697634c5bf59a6b327dce6b282d`. Native PostgreSQL 17
[run 38123254599](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38123254599),
job `114422189289`, completed SUCCESS: **263 C# checks**, including **65 BFF
checks**, **296 emitted events** matching the pinned Platform schema, all five
API HTTP smokes, and zero build warnings. Documentary run `38123254595` also
completed SUCCESS. The subsequent evidence update changes documentation only.

Executable checks cover independent receipt-key validation, wrong client key and
subject configuration rejection without token erasure, per-session admission
subjects on a shared adapter, cross-pool latched rejection circuit and trusted
reset, invalid receipt quarantine without predecessor replay, upstream-first
idempotent logout including lost response after Identity commit, and trusted
resource Session-401 handling. In addition to twenty decisions across two actual
processes producing one rotation and the five named kill boundaries, six random
real process kills ran without stage probes. Latest seed: `1208417327`; delays
75, 63, 133, 36, 9 and 33 ms. Recovery checked at most one rotation, no consumed
predecessor resend, and committed cleanup or terminal reconciliation of each
marker. This is sampled interleaving coverage, not exhaustive or replicated
failover certification.

The first candidate `bbae3b4a9212217c8623e9c02d91c6e9822b59ff`
([run 38122102541](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38122102541),
job `114418780270`) passed native checks but failed the existing recovery HTTP
timing maximum: a decoy completion took 359.66 ms against the unchanged 350 ms
bound. Its cause was not established. Added protected Development/loopback
server timing diagnostics and separate wire measurements; floor, maximum and
median/tail spread gates were not widened, and no automatic retry was added.
The diagnostic revision `871edb2` passed run `38122506072`.
A subsequent absent/wrong-diagnostic-key smoke in `d9f88dc` failed run
`38122911693` because its fixture reused an already exhausted admission subject
and received 429. Each privacy check now uses a fresh subject. The verified
4edc2c3 run passes both checks: no Server-Timing without the valid protected key.
The original timing outlier remains an unresolved observation, not a proven fix.

Latest HTTP completion p90 real/decoy/suppressed: 218.46/220.77/217.80 ms;
initiation: 218.89/220.07/221.87 ms. Twelve samples per group are regression
evidence only; production load/timing acceptance remains required. Diagnostic
work latency can reveal account-path differences and must not be enabled for
public traffic.

Deployment requires an independent Identity:RefreshReceiptKey, distinct from
the client credential and shared with the trusted BFF adapter. The circuit uses
durable state and a metric; metrics export, alert delivery and a private operator
HTTP host are not supplied. Its heuristic may also trip on legitimate bulk
revocation, and cannot restore sessions closed before its threshold. Browser
host/cookie/CSRF/Origin handlers, replicated database failover durability, actual
notification providers and contact-release policy remain release work. Upstream
logout and Session-401 core methods are now implemented, superseding that earlier
limitation; browser integration still must call them correctly. Platform PR #11
merge and canonical pin verification must precede Identity merge/release. Both
PRs remain drafts; no main merge or production change was performed.


## Automatic circuit recovery and immediate logout intent — 2026-10-11

Verified source `6fe9d69b3a680c796e801f98bea62121e4ecc3e0`, tree
`258db08d31625d1ddf0635014f787d862dec5ec4`: native PostgreSQL 17
[run 38124562388](https://github.com/amirfassadi/SmartCoreIdentity/actions/runs/38124562388),
job `114426124812`, completed SUCCESS with **280 C# checks**, including
**82 BFF checks**, **305 emitted-event validations**, all five API HTTP smokes
and zero build warnings. Documentary run `38124562418` also passed. Intermediate
runs `38124295931` and `38124393325` both completed SUCCESS. Their apparently
long-running status prompted a two-minute BFF harness watchdog; no deadlock or
stalled fixture was established, and no retry or acceptance-bound relaxation
was used. Subsequent evidence commits are documentation-only.

Migration 004 preserves old terminal states and adds irreversible LogoutRequested
with retained sealed material, plus circuit next_probe_at/probe_session. Checks
verify known-unconsumed refresh recovers automatically after credential repair,
twenty cross-pool contenders elect exactly one half-open probe, failed/disappeared
probes leave a minute of backoff, and only a validated elected success closes the
circuit. Owner budgets of 60 seconds or more are rejected to prevent overlap with
the recovery interval. The journal still forbids replay of unmatched dispatches.
Three separate unverified 403 sessions are required to open the deployment circuit;
one malformed 403 quarantines only its session. Other unverifiable receipt claims
retain the immediate integrity guard, and verified configuration receipts can
open immediately. No permanent circuit latch mode is enabled.

The native contention check held the circuit singleton row FOR UPDATE while twenty
distinct success sample writes completed through two pools: 3.14 ms elapsed.
Normal successful refresh no longer takes the singleton lock. This is a controlled
sample-write contention check, not a full refresh throughput benchmark, production
load test or exact synchronized failure-ratio measurement.

A paused real HTTP logout proves another instance rejects cached access before
the upstream response finishes. Lost-response coverage verifies committed
LogoutRequested, denied refresh/access, database rejection of reactivation, retained
material for idempotent retry, one upstream logout event and terminal erasure.
Logout completes independently of an open refresh circuit. No autonomous retry
worker or browser cookie handler is implied; the future host must expire the cookie
and schedule bounded completion of retained server intent.

Random process-kill seed in this run: `1588439761`; all six trials and the named
kill boundaries passed. Latest reset completion p90 real/decoy/suppressed:
222.91/222.90/223.73 ms, maxima 222.91/223.56/284.99 ms. Initiation p90:
219.63/218.77/222.08 ms. Existing timing gates remain unchanged; twelve samples
per group do not prove channel resistance or load behavior.

Release gates remain browser-host cookie/CSRF/Origin integration, alert/metrics
delivery and operator hosting, production load/timing, replicated database failover,
notification delivery and contact-release policy. The runbook in BFF_CORE.md
forbids deleting journals or force-retrying predecessors. A future permanent latch
policy needs the operator host, delivered alert and an accepted operational runbook.
Platform PR #11 merge/canonical pin verification still precedes Identity release;
no main merge or production/VPS change was performed.
