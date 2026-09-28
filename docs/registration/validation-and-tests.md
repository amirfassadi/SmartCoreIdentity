# Registration validation and verification plan

Version: 0.1.0 — DRAFT; required tests, not implemented/passed tests.

## Preserve the uploaded classification framework

The uploaded 12 v1.0.8 §2 distinguishes a business requirement from its enforcement checkpoints. Retain one classification per checkpoint: Domain Rule, Policy Rule or Infrastructure Constraint. A requirement may have multiple separately classified checkpoints. Do not label every check “precondition”, and do not treat a pre-check as concurrency protection.

| Checkpoint | Category | Classification | Expected contract |
|---|---|---|---|
| Exactly one selected contact in proposed MVP encoding | Input / cross-field | Domain Rule | Email-only or mobile-only; reject both/neither/null |
| Contact syntax and canonical representation | Input | Domain Rule | Same canonical key for proof, lookup and uniqueness |
| Password/name configured bounds | Input / business | Policy Rule | Reject before staging expensive work where possible |
| Absolute proof lifetime and cumulative budget | Business | Policy Rule | Finite configured limits, no resend extension |
| Atomic attempt increment/consumption | Infrastructure | Infrastructure Constraint | Parallel workers cannot double-consume/exceed budget |
| Duplicate normalized-contact check after proof | Business | Domain Rule | No second Person; safe conflict routing |
| Contact storage uniqueness | Cross-instance | Infrastructure Constraint | Authoritative under concurrent confirmations |
| Ownership triple and workflow/material binding rule | Cross-Aggregate | Domain Rule | No partial ownership or dangling material reference |
| UoW atomic writes and Outbox | Cross-Aggregate | Infrastructure Constraint | Crash/rollback preserves all-or-none ownership |
| Ready and current active Credential at authentication | Business / cross-Aggregate read | Domain Rule | Active Credential alone is insufficient |
| Immutable provisioning winner and guarded mutation | Business | Domain Rule | Loser cannot overwrite first winner |
| Credential winner/phase transaction and CAS | Infrastructure | Infrastructure Constraint | No independent write bypass |
| TLS and secret-safe persistence/audit | Infrastructure | Infrastructure Constraint | No plaintext password/proof in durable output |

## Configuration reconciliation

No new numeric operational values are approved here. Preserve and compare, rather than silently weaken, local 10's details: Argon2id RFC 9106-only policy and benchmarkable cost parameters; minimum length 10 plus letter/digit; access TTL 1 hour; refresh TTL 7 days; production rotation; three retries with 500ms base. The platform proposal differs: minimum length 15, access TTL 900 seconds, Session absolute lifetime 86400 seconds/no extension, nonrotating refresh, eight provisioning attempts with 2-second initial backoff, and independent material/proof cleanup budgets.

These are competing documented policies, not all accepted architectural facts. Security/operations review must resolve the applicable values and Session behavior before generation/deployment. Argon2id algorithm/cost selection must not be silently replaced merely because the shorter proposal leaves KDF selection open. The legacy “core retry 0” is not a ban on authorized retry after an uncertain response; replay/reconciliation must establish whether a commit exists.

ADR-0004-required administrative/acknowledgment settings remain finite but unset until owner policy approval: attempt/backoff limits, permit and execution lifetimes, operator/target admission limits, result retention, audit retention/backlog bounds, alert threshold and operator/service trust configuration. Disabled recovery is not a waiver of rollout requirements.

Person-facing MFA remains outside this milestone; operator MFA/step-up is required by ADR-0004. Contact/OTP delivery is infrastructure for registration, not a dependency on completion of the business Communication module. Select provider, secrets/KMS and deployment stack separately.

## Required behavioral cases

| ID | Scenario | Required result |
|---|---|---|
| R01 | Email-only and mobile-only initiation | No ownership before valid proof; unused contact not required |
| R02 | Invalid/both/neither/null contact, malformed input | Safe validation failure; no durable ownership |
| R03 | Existing vs unused contacts; initial/resend throttling | No pre-proof existence leak across body/status/timing/delivery policy |
| R04 | Parallel wrong codes, resend, expiry boundary | Shared budget; old/expired proof denied; expiry unchanged |
| R05 | Duplicate simultaneous valid confirmation | One ownership triple/workflow/bind; authorized replay returns existing result |
| R06 | Failure at each ownership write or lost commit response | Atomic rollback or same committed result; no false definitive rollback |
| R07 | Cleanup racing material ownership transfer | No live registration reference deleted; unused material disposed within bound |
| R08 | Active Credential, workflow still PendingCredential | Login and Session issuance denied |
| R09 | Automatic versus setup candidate race | One early winner, no password replacement by loser; truthful credentialOutcome |
| R10 | Expired replay followed by fresh conflicting attempt | No second Person; second password discarded; distinct authorized setup |
| R11 | Ready CAS crash/duplicate worker | One immutable ReadyFactId/logical event; stable EventId under redelivery |
| R12 | Lost acknowledgment, then replay after password change | Guard persists until valid ack; duplicate ack does not restore old Credential |
| R13 | Historical AlreadyCompleted while Identity Pending | IntegrityConflict; no fabricated Ready |
| R14 | Exhaustion/material expiry | Ownership preserved; safe setup/re-drive path; no TTL unlock |
| R15 | Forged/expired admin permit, revoked grant, changed target | No new unauthorized effect; exact binding and bounded execution enforced |
| R16 | Admin invalidation races ownership commit | AlreadyCommitted preserves transferred material |
| R17 | Local audit failure / external backlog exceeded | No unaudited mutation; bounded buffering then pause |
| R18 | Ready event attribution/timestamps/mobile-only payload | Actual actor/context, Ready OccurredAt, ownership timestamp, no SessionReference |
| R19 | Ready login → self read → logout | Valid Session only after authentication; foreign self access denied; logout invalidates access |
| R20 | Secret disposal/logging and low-entropy proof leak | Keyed online verifier; no retained raw code/password/bearer; disposal verified |

Tests must use actual persistence concurrency/crash boundaries for atomicity claims, not only mocks. Token lifetime/rotation/refresh tests depend on resolving the Session-policy conflict. Public ordering/replay tests depend on T16; EventId dedupe alone does not prove ordering. Full 065, cryptographic, runtime, consumer and deployment validation are not replaced by documentation hash/link checks.

## Non-normative user examples

Mobile-only initiation returns AwaitingVerification; the client enters the code with the original binding. Verification may return 201 PendingCredential: show “finishing registration”, not authenticated success. Authorized replay within the window may later show Ready, then the user explicitly logs in.

If that response was lost and proof expired, a fresh verified attempt returns RequestSetup for the existing pending account. Its staged password is discarded. A distinct setup challenge authorizes completion; ExistingWinner means the new password was not installed. No example may instruct a client to guess account existence or poll by registrationId without authorization.
