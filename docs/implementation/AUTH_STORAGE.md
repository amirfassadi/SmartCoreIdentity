# Authentication storage and transaction ownership — A

Date: 2026-10-10. Owner: Amir (@amirfassadi), project/architecture owner under 051 §7. [Explicit A disposition](AUTH_TRANSACTION_DECISION.md). Source inputs: Platform `724ff849a9ddd6741fc5d84d6b54c366dcb774be`; Identity `968b918858345927a3624c3b8b8688cab88d9501`. This revision adds storage/gate foundations, not login, reset, Credential replacement or a production BFF.

## Transaction owners and write authority

| Boundary | Owner and sole application writer | Commit responsibility |
|---|---|---|
| Ownership/Ready | Identity registration service/Ready adapter | Existing Person/Organization/Membership ownership transaction, later Ready and Outbox transaction |
| Initial Credential/ack | Credential adapter | Existing immutable winner/guard and independent acknowledgment transaction; not changed by A |
| Session/coordination | Identity authentication application, through Session storage adapter | Person issuance state, password-change intent/schedule, Sessions, refresh families/generations and restricted refresh event rows in one local Session transaction |
| Later password mutation | Credential adapter, independently committed | Bound replacement operation and authoritative idempotent outcome lookup; cannot share the Session transaction under A |

Amir is the accountable technical/architecture owner. Deployment database roles and service credentials remain release work; a module write rule is not a claim of database-enforced production isolation. `IAuthenticationIssuanceGate` is the replaceable Session-storage interface. Its PostgreSQL implementation takes the caller-owned **Session** transaction; it neither opens a Credential transaction nor permits cross-module writes. If the backend changes, its storage-specific transaction parameter changes within this adapter boundary, not domain models.

## Migration 003 model

| Record | Purpose and invariant |
|---|---|
| `auth_issuance_state` | Supporting Person coordination: monotonic epoch, optional pending operation. Not a Person attribute/new Aggregate. A pending operation must belong to that Person and target that epoch. Releasing it requires a Reconciled intent; timeout is not reconciliation. |
| `auth_credential_change_intents` | Supporting application workflow admitted only after verified proof: stable operation/request MAC, expected Credential ID, target epoch, opaque protected-material reference, phase, durable retry schedule and safe classification. No raw proof or password. Credential ID is evidence, not permission. |
| `auth_sessions` | Session Aggregate storage: Person, issuance epoch, authenticated BFF client, immutable creation/86400-second deadline, last successful foreground refresh, terminal closure. Refresh cannot extend the deadline or move foreground time backward. |
| `auth_refresh_families` | One Session-owned supporting family per Session. Deadline equals its Session deadline. Revocation is terminal and generation cannot go backward. Eligibility always reads Session + family; a token row's Current flag alone grants nothing. |
| `auth_refresh_generations` | Key-versioned 32-byte keyed verifier only; purpose binds family, generation and authenticated client. At most one Current generation per family. Consumed predecessor cannot become Current, change verifier or change deadline. |
| `auth_refresh_security_events` | Only the existing internal refresh schema fields: stable event ID, Session/family IDs, generation, timestamp, type and reason. Family must belong to Session. No arbitrary payload or bearer/verifier material. Outside public Identity event catalog and T16. |

Migrations 001/002 and public registration contracts are preserved. `--migrate` applies version 003 sequentially; it does not start login/reset. Migration role owns DDL; Session adapter owns its tables after deployment roles are supplied.

## Lock order and eligibility

Acquire `auth-person:<PersonId>` transaction advisory lock first, then initialize/read the Person issuance row `FOR UPDATE`, then Session, family and generation rows in that order. Hold it until the **Session transaction** commits or rolls back. Multiple-Person work sorts Person IDs first. All future login, refresh, logout, revocation, mutation admission and sensitive-operation writers must use this order; no process-local mutex is a replacement.

The returned epoch snapshot is usable only inside that transaction. Pending means issuance denied. An unblocked epoch does not itself prove Ready, active Person/Credential, password, client, token or Session eligibility. Login must validate against current Credential evidence and revalidate after acquiring the gate; proof gathered before an epoch change must be discarded. Refresh checks signed/bound token evidence, matching Session epoch, current family/generation, absolute/idle deadline, Ready and active Person/Credential under the gate.

Sensitive operations require current online Session/family status and deadline, valid authorization/proof, current epoch equality and no pending fence. Change/reset admission also respects ReadyAcknowledged. A recovery-proof reset can lack an access Session but must bind its admission epoch and revalidate it. General resource-server access remains bounded by 900-second access expiry; online checks here do not promise immediate universal revocation.

## Fence admission and scheduled recovery

Reset initiation/delivery/invalid proof **cannot** insert an admitted intent, advance epoch, activate a fence or close Sessions. Only after verification and authorization does the internal mutation coordinator, under the Person gate, revalidate epoch/Credential, persist the bound admitted intent, advance epoch once, close all existing Sessions/families and schedule recovery in the same Session commit. The schema's verified timestamp is required evidence metadata, not proof verification by itself. No public or admin API accepting a caller's `verified=true` is introduced.

Credential replacement then uses a separate idempotent operation. Timeout/crash leaves issuance denied; a worker queries the authoritative outcome before retrying or releasing the fence. `next_attempt_at`, attempts, stage and safe classification make this work durable/observable to authorized operators. `FailedClosed` is terminal operator attention, not authority to reopen issuance. Only confirmed/reconciled outcome permits resolution; old Sessions are never restored. A resolved definitive failure may reopen future login against the unchanged Credential only after authoritative reconciliation, while already closed Sessions stay closed.

The scheduler, proof adapters, Credential operation/outcome protocol and operator-authenticated status endpoint are **not implemented in this storage-only slice**. No real fence can be activated through an exposed endpoint. Their next delivery must test crash at every phase, stuck/unknown results, bounded retry escalation and restricted status. Login/refresh failures use the same outward shape for fenced and other unauthorized states; no account/reset-progress enumeration. Do not automatically clear a stuck fence on a timer.

## Strict reuse: zero grace, finite recognition

`recognition_until` equals the immutable family/Session deadline. Retain the predecessor verifier through that point to identify an authenticated, correctly client-bound spent secret. Any such presentation before that deadline revokes the whole family and closes its Session in the same transaction as RefreshTokenReuseDetected. That event includes the revoked family; no new SessionRevoked event is selected. Successful rotation emits RefreshTokenRotated for the successor generation. Both are internal facts. Wrong-client or guessed IDs never authorize family revocation.

**Grace window = 0 seconds**; no cached response, successor disclosure or retry allowance. A lost response after commit can force reauthentication. This is the owner's preserved strict policy, not a measured mobile failure rate or claim that reuse proves theft. BFF instances must serialize refresh and clear ambiguous continuation state instead of blindly retrying. Any later grace proposal requires explicit policy review.

At/after the absolute deadline no generation is usable. Recognition records may be purged then through a controlled cleanup adapter; security audit has a separate retention decision. Verifier keys must remain available through recognition lifetime. Key rotation/deletion, finite audit retention, backup/WAL handling and real BFF authorization require separate evidence. No automatic production audit purge is added here.

## Evidence scope

Executable storage tests cover migration replay, one Current generation, matching family/Session deadlines, consumed-token immutability, immutable Session deadline, terminal closure, monotonic epoch, unresolved-fence release rejection, recovery metadata and per-Person gate serialization. These are storage/gate checks; they do not prove login/refresh token issuance, proof admission, background reconciliation, abuse limits, BFF secrecy or sensitive endpoint enforcement. Those remain the next authentication slice's tests.
