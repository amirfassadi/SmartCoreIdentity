# Development password change and recovery

Architecture A remains selected. Migration 005 adds immutable request/material bindings, Credential-owned terminal receipts and a Credential-owned PasswordChanged outbox. It preserves migrations 001–004. The initial Credential ID and initial winner history remain unchanged. The separate Credential adapter requires Ready plus the historical ReadyAcknowledged guard before mutation; it does not rewrite registration or acknowledgment evidence.

## Admission and transaction owners

`Identity__PasswordChangeEnabled=true` additionally enables POST `/auth/password/change` with the authenticated test BFF key and signed access proof. See [authentication OpenAPI 0.3.1](../../contracts/authentication.openapi.yaml). The request is `{operationId,currentPassword,newPassword}`; the nonempty UUID is the stable idempotency identity. Policy is the existing 15–128 character development policy. Validate policy, verify the old password and derive the new Argon2 encoding before taking any issuance lock. Under the Person gate, revalidate JWT lifetime after KDF/lock wait and recheck Credential identity/hash, Ready/ack, signed epoch, Session ownership/client/status, idle/absolute deadline and active family.

| Durable boundary | Owner and locks | Evidence |
|---|---|---|
| Admit/fence | Session; only auth-person lock, then Session/family rows | Bound request MAC, Credential ID/hash MAC, encrypted replacement Argon2 encoding, proof Session/client, one epoch advance, all Sessions/families closed, due job |
| Replace | Credential; only credential registration lock, then Credential row | Same bound operation, expected hash MAC, hash replacement plus immutable Applied receipt and one PasswordChanged fact in one commit |
| Reconcile | Session; new auth-person transaction after Credential transaction disposal | Matching authoritative receipt, Reconciled stage, pending fence released without lowering epoch, encrypted material erased |

No namespace lock is held across the other transaction or across KDF work. The development Credential adapter reads immutable admission evidence in the same database but commits independently. Splitting stores later requires an authenticated operation/outcome adapter, not a shared transaction.

Request version 2 MAC binds only operation/person/Session/client. New-password replay equality uses Argon2 verification of the bounded stored replay encoding, outside all locks; raw passwords are not MAC inputs. See [admission and migration](AUTH_ADMISSION.md). The replacement encoding is AES-GCM-protected with the existing material key and operation-bound purpose, with a fixed 24-hour development lifetime. Receipts bind operation/person/Credential/request and expected/replacement hash MACs. Receipt lookup precedes comparison with current Credential or material expiry: old operations remain Applied after later changes. Passwords, hashes, verifiers and protected material do not appear in events or responses. Production key rotation, retention and database access roles remain release work.

Admission closes Sessions with reason PasswordChanged while replacement is pending. That reason denotes the admitted operation; only an Applied receipt and its PasswordChanged fact prove replacement. Rejection never restores Sessions or lowers epoch. Replay with the original still-valid signed Session/client JWT and matching new password returns workflow status even after the Session closes; different new-password or metadata binding conflicts. The admitted original current-password text is not rechecked on replay. After access expiry owner replay is unauthorized; operator status remains available. Reconciled means a confirmed terminal outcome, which can be Applied or RejectedNotApplied, not necessarily successful replacement. This is not a complete BFF user-facing progress flow.

## Scheduled recovery and operator

The worker is enabled with the slice unless `Identity__PasswordChangeWorkerEnabled=false`. Ordinary unexpected exceptions are classified OutcomeUnknown at both job and worker-loop boundaries; cancellation propagates. Atomic due-job claims persist attempt count and a 60-second lease. After a crash the same operation resumes after lease expiry. Eight attempts, integrity conflict or expired/missing material produce FailedClosed and preserve the fence. A crash during attempt eight becomes visibly FailedClosed after lease expiry. Timeout never unlocks. Expired material is erased; a committed receipt can still reconcile without it. Logs contain safe classification/type only.

The Linux Development-only operator adapter requires the service-private Unix socket, explicit application port, and `X-Auth-Operator-Key` matching a distinct configured secret of at least 32 characters. The BFF key cannot authorize it. GET/POST `/dev/auth/recovery/{operationId}` are absent unless the slice is enabled. Status exposes operation and stage only. POST supports:

- `Retry`: requeue the same unresolved operation under the Person gate without clearing epoch/fence. The worker honors existing receipts before applying anything.
- `ResolveNotApplied`: acquire the Credential lock in a separate transaction, honor any existing receipt, or verify unchanged expected Credential and commit a terminal RejectedNotApplied receipt. Reconcile in a new Session transaction. Stale workers must honor this rejection and cannot apply afterward; an Applied receipt is never undone.

Operator audit records requested action and effective receipt outcome and commits with the Session action/reconciliation. There is no ForceUnlock, direct-SQL bypass, replacement operation or operator-supplied password. Integrity conflict remains blocked with RECOVERY_CONFLICT; repairing conflicting storage is not implemented and cannot be replaced by a timer. A single development key is not named production operator authentication or a full operations UI.

## Verification and remaining scope

The integration harness performs actual replacement/recovery, separately from earlier storage fixtures: policy/proof before fencing, identical-admission race, all-Session closure, paused old-password login crossing replacement, restart after admission, lost Credential result, historical receipt replay, immutable receipt, eighth-attempt failure/operator retry, material expiry/not-applied resolution, stale-worker rejection, Applied result encountered by the operator, audited actions and refresh/mutation race. The HTTP smoke covers the real worker, BFF/proof/input admission, operator key isolation, new-password login and old Session/refresh denial. Outcomes are in [verification](VERIFICATION.md); configured tests alone are not a PASS.

Reset initiation/delivery/proof/completion and reassigned-number recovery are absent. Eligibility transitions must follow [authentication runtime](AUTH_RUNTIME.md), which also lists rate limits, LoginFailed volume/privacy, BFF distributed single-flight and self-read contention. Public onboarding remains closed. This slice does not authorize production startup, a main merge or VPS deployment.
