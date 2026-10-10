# Registration backend — implementation baseline 0.3.0

Date: 2026-10-10 (registration, setup and development authentication revision). Owner: Amir (@amirfassadi), project/technical owner.

## Authority and delivery boundary

The owner's 2026-10-09 instruction explicitly requests backend/database implementation now, registration first, frontend later, rapid launch, and finalization of supporting documentation. It supersedes earlier generic 'continue' messages as the authority to begin implementation. It does not retrospectively certify unsigned ADRs, full 064/065 validation, runtime tests, or production readiness. Engineering choices below are the implementer's selections under this instruction, not fabricated individual owner signatures.

Original phase-1 inputs: Identity `f0781c1415b3a784788c326b93c2cd45bd5206cd` and Platform `240151e25c5d9f67a58a6f4eea729a667934cc2b`. Active event input: Platform schema 1.2.2 at `49ea40a50854c447b18afbdd7c2f3116dbee3136`, recorded in contracts/events.input.provenance.json. Historical/uploaded documents remain preserved. For the implemented phase, this baseline, `contracts/registration.openapi.yaml`, migration and executable tests describe actual behavior. Older 'no implementation exists' statements are historical. Proposed future-capability contracts remain proposals.

This is the final engineering baseline for **internal, disposable-account registration phase 1**. The complete Identity MVP is not declared final or production-ready. This distinction is enforced by refusing Production startup. No real user onboarding may open until password reset is implemented and accepted; internal accounts are test data and may be deleted. Delivery phasing does not reduce the architectural review scope of the full Identity Blueprint.

## Selected implementation

- ASP.NET Core / .NET 10, PostgreSQL 17, Npgsql; one application host with explicit Identity/Credential transaction boundaries. No Redis or message broker is needed to test this phase.
- Start / verify / resend match the pinned contact-registration request shapes. Email and E.164 mobile are equal alternatives; exactly one is required. Frontend has not been implemented.
- Email uses a documented case-insensitive canonical identifier (trim and lowercase); mobile requires canonical E.164 input. DisplayName is trimmed. No country-specific domain rule.
- Atomic Person + Personal Organization + Owner Membership + workflow + proof consumption + material transfer + ownership event outbox + provisioning work.
- Separate Credential commit, Identity Ready commit, and Credential acknowledgment commit. One durable initial winner per registration; no mutation/revocation endpoint bypasses its guard. Hosting both stores in one database does **not** adopt the still-open single-transaction co-location alternative.
- Stable event identifiers and timestamps; `PersonRegistered.OccurredAt` is Ready time. Ownership time remains separate. Public event publication is disabled; facts remain in Outbox. No T16 transport option or consumer order guarantee is silently selected.
- Registration never returns access/refresh tokens. Authorized verification replay reads Pending/Ready within its original proof window. No public registration-id lookup.
- All supported writes are application-owned. The local Compose setup uses a development database role; separated runtime/migration/Credential privileges are still a deployment requirement.

## Finite development policy

| Parameter | Implemented value |
|---|---|
| Password | 15–128 characters; Argon2id v19, 64 MiB, 3 iterations, parallelism 4, random 16-byte salt, 32-byte result |
| OTP | Cryptographically random 6 digits; purpose/session-separated HMAC-SHA256 verifier |
| Verification | 10 minutes absolute; 5 cumulative verification attempts, including authorized replays |
| Resend | At least 60 seconds apart; maximum 3; rotates proof, preserves expiry/attempts |
| Start admission | 5 per canonical contact/hour, persisted across API instances |
| HTTP admission | 30 requests/IP/minute per API process; max body 16 KiB; no forwarded-IP trust configured |
| Request binding | Client-generated 32 random bytes, unpadded base64url; stored only as scoped HMAC |
| Idempotency key | 32–128 URL-safe characters; replay requires matching full request and unexpired proof |
| Protected material | Prepared Argon2 hash protected with AES-256-GCM and owner-specific associated data |
| Post-commit material lifetime | 15 minutes from ownership commit; success erases immediately |
| Provisioning retries | 8 attempts; persisted exponential delay starting at 2 seconds; terminal failure flagged |
| Cleanup | Each worker tick clears expired OTP/binding/request verifiers, sealed material, sealed delivery codes |

These values resolve the archived conflicting values for this phase only. Password-session policy, idle/refresh rules, reset security, provider pricing/delivery limits and operator recovery policy remain outside the implemented surface. Deployment requires KDF capacity testing, managed key rotation/retention, verified abuse controls and minimum/maximum configuration review. Garbage collection is logical deletion; backup/WAL expiry and cryptographic erasure are not claimed.

## Boundaries deliberately left for the next deliveries

1. The formal audited administrative recovery path. `CONTACT_UNAVAILABLE` does not replace the original password. Self-service setup is now implemented under the distinct proof contract below. Current failures remain visible in `workflow_jobs`; no direct-SQL repair is an approved support procedure.
2. Development login, refresh, logout and `/me` now use the Person issuance gate: [runtime](AUTH_RUNTIME.md). Still pending: change-password/reset coordinator, Credential outcome protocol, reconciliation worker and authenticated operator resolution, actual BFF, verified-contact reset **before public registration**. Reset must address reassigned mobile numbers and revoke all Sessions/families.
3. Real SMS/email provider with authenticated delivery, retry/TTL behavior and existence-independent responses; current encrypted delivery outbox is a test adapter, not real delivery.
4. Event dispatcher, T16 transport/wrapper and consumer conformance. Never mark an event published without actual durable transport acknowledgment.
5. Restricted operator recovery, MFA/step-up, append-only external audit, service trust, alert ownership, backup/restore reconciliation and retention.
6. Full Blueprint/rule disposition (including P02/P03), architecture/security review and 064/065/066 evidence for the complete MVP. No `READY_FOR_GENERATION=true` claim.

The request authorizes forward progress on these items. It does not turn unchecked items into completed work. See [execution guide](RUNBOOK.md) and [verification evidence](VERIFICATION.md).

The owner explicitly selected A on 2026-10-10: [authentication transaction decision](AUTH_TRANSACTION_DECISION.md). Migration 003 and [storage/gate foundations](AUTH_STORAGE.md) preserve separate Credential commits; Session coordination owns its own transaction. No authentication/reset endpoint or background password reconciliation is enabled by this storage slice. B requires evidence and a separate scoped ADR revision.

## Registration setup/completion — revision 0.2.0

`POST /auth/register/setup` accepts the original code and binding from a freshly verified conflicting attempt. It only admits a separate setup challenge for the existing PendingCredential registration, with a newly delivered code different from the registration code. Delivery uses the stored registration/Person association, never a caller-supplied destination. Verified conflict now returns RequestSetup or SignIn; the conflicting attempt's password is always discarded.

`POST /auth/register/complete` consumes setup proof and binds one immutable operation to its idempotency key and server-keyed request fingerprint. It stages only encrypted Argon2 output. Candidate acceptance, disposal of its delivery code and durable worker redrive commit together. Subsequent provisioning, Ready and acknowledgment retain separate transactions. Existing winners are preserved; completion reports CandidateSelected, ExistingWinner or pending Undetermined. Neither route is password reset and neither returns tokens.

Setup proof is absolute 10 minutes with five shared attempts (including replay); accepted material is separately bounded to 15 minutes from acceptance. A committed candidate may finish after proof expires; expiry never authorizes a new acceptance or public replay. Changed key/password is a conflict, not another candidate. Challenge replay returns the same ID/expiry without redelivery. Setup proof/material use distinct HMAC/AES purposes. Losing material is erased after a winner commits; expired proof/delivery material is erased by the worker. Existing database/WAL/backup erasure limitations remain.

Forward migration 002 adds supporting setup records and bounded conflict mapping. The migration runner skips already-installed versions; 001 was not edited. Development fake setup inbox has the same loopback/opt-in-key restriction as the registration inbox. Formal operator recovery and production delivery remain unimplemented. See [setup/completion](SETUP_COMPLETION.md).

## Authentication adapter — revision 0.3.0

Migration 004 fixes immutable closure/consumption evidence without modifying deployed-in-tests 003. Opt-in development BFF routes now implement Ready-only login, online self, S2 refresh with zero grace and foreground idle, and bound logout replay. Access JWT TTL is at most 900 seconds, Session lifetime is 86400 seconds and foreground idle is 1800 seconds. Unknown/wrong-client refresh does not revoke a family. Session transaction locks never overlap Credential namespace locks. Separate reuse latency observations support later measurement; they do not change policy or internal event schema. Public authentication facts remain unpublished in their own Outbox. See AUTH_RUNTIME.md and VERIFICATION.md for scope and actual evidence.

Password policy must pass before future mutation intent/fence admission. FailedClosed has no timer escape: authoritative operator reconciliation is required. No public reset until both worker and authenticated operator resolution are delivered. Current test fixtures do not implement those paths. Production and real-user onboarding remain disabled.

## Password change and recovery — revision 0.4.0

Migration 005 and an additional disabled-by-default development flag add proof/policy-gated password change, separate Credential replacement/receipt commits, scheduled reconciliation and loopback operator Retry/ResolveNotApplied. Closed Sessions are never restored. PasswordChanged commits once with an Applied Credential receipt. Reset proof/delivery/completion and production authorization remain absent. AUTH_RUNTIME.md records separate authentication rate admission, irreversible eligibility transitions, distributed BFF single-flight, LoginFailed budgets/privacy and self-read contention as pre-BFF/pre-release conditions. See PASSWORD_CHANGE.md for exact boundary, expiry and recovery semantics; actual results are recorded in VERIFICATION.md.

## Authentication admission and operator isolation — revision 0.5.0

Migration 006 adds shared caller budgets, Person failure/backoff evidence, password-replay version 2 with Argon2 equality and removal of legacy raw-password MAC oracles, and effective operator audit outcomes. Unexpected worker exceptions preserve OutcomeUnknown recovery. The development operator API is isolated on a private Unix socket and refused on every TCP request, including loopback with a valid key. OpenAPI 0.3.0 requires a trusted BFF subject assertion. Production capacity/privacy/telemetry and BFF integration remain unaccepted; reset and onboarding remain disabled. See AUTH_ADMISSION.md and the corresponding verification record.

## Sealed replay and burst allowance — revision 0.6.0

Migration 007 removes the old clear replay column and stores newly admitted replay verifiers only as operation-bound AES-GCM material. Upgrade invalidates previous request replay without disrupting independent encrypted Credential recovery or receipts. Stop older application instances before migration. KDF work allows four running jobs and sixteen queued waiters with a two-second deadline; capacity failure is 503 AUTHENTICATION_BUSY, separate from caller-budget 429 RATE_LIMITED. OpenAPI is 0.3.1. Native evidence is in VERIFICATION.md. RESET_PROOF_DECISION.md proposes enrolled recovery-code plus contact OTP proof for owner disposition; no reset endpoint is added. Production load, retained historical data erasure, longer failure escalation/alerts and real BFF remain unaccepted release conditions.

## Recovery enrollment and initiation — revision 0.7.0

Owner selected OTP plus a pre-enrolled recovery code for both email and telephone accounts, with shared Person failure limits, one-time consumption/replacement after fresh login, notifications, explicit loss-risk acknowledgement and documented contact-release consequences. Migration 008 implements authenticated current-password enrollment/replacement, monotonic version and epoch binding, immutable receipts and atomic notification tasks. New enrollment responses reveal a 128-bit code once; replay returns only receipt status. Login/self expose recoveryEnrollmentRequired for the real BFF's later enforcement.

Opt-in reset initiation returns uniform accepted challenges, binds epoch/code version and ten-minute deadline, and queues purpose-separated encrypted OTP tasks only for eligible accounts. Three deliveries per Person per fifteen minutes supplement BFF category limits. Initiation never fences or closes Sessions. Test inbox is protected and Development-only. OpenAPI is 0.4.0. This revision originally left completion pending; migration 009 below now delivers dual-proof acceptance, shared Person proof limits, reservation/consumption and completion notification tasks after the selected event input was pinned. Provider notifications/delivery, real BFF enforcement and release/privacy/retention/contact-release acceptance remain pending. No public onboarding or production startup is enabled.

## Dual-factor reset candidate, 2026-10-10

Platform schema 1.2.2 is pinned from `49ea40a50854c447b18afbdd7c2f3116dbee3136` with source provenance. Migration 009 adds deletable decoys, shared Person proof limits, immutable reset acceptance and receipt-backed recovery-code reservation/consumption. The worker and private recovery operator are reused; no synthetic Session is created. See [PASSWORD_RESET.md](PASSWORD_RESET.md). Runtime CI evidence for this candidate is pending; previous test counts apply only to their recorded commits.
