# Registration implementation work plan

Version: 0.1.0 — DRAFT — 2026-09-28
Scope: first delivery milestone in SmartCoreIdentity. This is a work breakdown, not an accepted ADR, active machine specification, or readiness certificate.

## Authority and starting point

Use [README](README.md) for pinned platform revision and decision status. Implement exact public shapes from the pinned platform `Identity/openapi.yaml` and `08_API.md`, internal shapes from `07_Contracts.md` and `services.schema.json`, and event shapes from `events.schema.json`. [Workflow contract](workflow-contract.md) describes behavior; [validation and tests](validation-and-tests.md) defines R01–R20. Do not regenerate from the archived uploaded `capability_machine.yaml`.

The first milestone ends at verified registration, durable ownership, Credential readiness, explicit login, authenticated self read and logout. The platform's broader Identity MVP also includes refresh, ChangePassword and profile update; these are subsequent work inside the same MVP, not deleted Commands. No Kimia Business, appointment or staff rule is implemented here.

## Dependency order and completion evidence

| Slice | Build | Demonstrate before marking complete |
|---|---|---|
| 0. Contract baseline | Record exact SmartCorePlatform and SmartCoreIdentity commit SHAs; settle applicable ADR-0002/0003 governance and check ADR-0004 scope; reconcile the proposed API/service/event schemas against this package. | Reviewed contract diff and decision record; no historical Blueprint readiness inferred. |
| 1. Verification | Canonical email OR E.164 mobile, DisplayName/password validation, protected material staging, purpose-bound code, binding secret, initiation/resend and shared attempt budgets. No ownership writes. | R01–R04 and R20, including parallel attempts, expiry, non-enumeration and secret disposal. |
| 2. Atomic ownership | RegistrationApplicationService single Unit of Work: Person, Personal Organization, Owner Membership, PendingCredential workflow, consumed-proof mapping, material-reference transfer, ownership events and provisioning Outbox. Database uniqueness on canonical contact and guarded proof consumption. | R05–R07 and R10 with real database concurrency, rollback injection and lost-response replay. No partial triple or dangling committed reference. |
| 3. Credential worker | Outbox dispatcher, authenticated EnsureInitialCredential/GetInitialCredentialResult, durable registration/Person winner record, one-active constraint, bounded retry and recovery-needed state. Every Credential writer honors the same pre-Ready guard. | R08–R10, R13–R14: lost response, concurrent auto/setup candidates, one immutable winner; pending never logs in. |
| 4. Ready and recovery | Reconcile active guarded winner; atomic PendingCredential→Ready CAS with immutable ReadyFactId, one logical PersonRegistered and acknowledgment Outbox. Separate one-time user setup path. Authenticated acknowledgment releases guard; restricted admin invalidation/reconciliation, job and append-only audit follow ADR-0004. | R09–R18, including crash between transactions, delayed/replayed acknowledgment after password change, permit revocation, audit outage and cleanup race. A queued admin job is not reported as recovered. |
| 5. Login and self | AuthenticatePerson only when Ready and current active Credential; create Session, return tokens; authenticated GET /me and logout. LoginFailed is a Security Event with safe conditional identity reference. | R08, R19–R20, plus token/session invalidation and generic outward failure. No Session from registration response. |
| 6. Complete platform MVP | Resolve Session policy, then refresh and expiry; implement DisplayName-only profile update and ChangePassword subject to acknowledgment guard. Verify the remaining five Queries, six Commands and ten event obligations of platform ID-14. | Session policy tests, R01–R20 regression, event consumer compatibility, exact schema conformance and full applicable 065 validation. |

Slices are dependency order, not permission to deploy partial security controls. In particular, slice 3 cannot be released without the completion/recovery paths and monitoring of slice 4.

## Persistence and transaction checklist

- Identity transaction A: proof consumption, ownership triple, workflow, transfer reference, replay mapping, ownership-event Outbox and provisioning work commit or roll back together. Staged material has independent pre-commit expiry; cleanup cannot remove a transferred live reference.
- Credential transaction B: initial active Credential, immutable winner binding, operation result and durable guard commit together. Unique Person active-Credential and registration winner constraints are storage-enforced.
- Identity transaction C: Ready CAS, timestamp/winner evidence, stable PersonRegistered event identity and Ready-acknowledgment Outbox commit together. PersonRegistered OccurredAt is Ready time; OwnershipCommittedAt remains transaction A time.
- Credential transaction D: verify authenticated committed Ready fact and winner generation, persist acknowledgment and release guard together. Duplicate matching acknowledgment remains valid after later password replacement without restoring the old Credential.
- Administrative effects: admission permit, current authorization, target/version checks and bounded durable job; each local mutation and its append-only audit journal commit together. No direct database repair path.

No distributed transaction is assumed between A, B, C and D. Reconciliation after uncertain remote outcomes uses durable result lookup, stable operation identities and bounded retry. An account stuck after A remains PendingCredential and non-authenticatable; it is not deleted to hide a failed provisioning attempt.

## Decisions and gates that cannot be implemented by guessing

| Gate | Decision/evidence needed | Impact |
|---|---|---|
| GOV | Accept applicable ADR-0002/0003 scope under 051; retain ADR-0004's separately accepted scope. | Architecture baseline for ownership, contact and lifecycle. |
| T16 | Decide PersonRegistered/PersonUpdated ordering and consumer replay contract. | Event allocator, publication and consumer tests. |
| SESSION | Resolve uploaded rotation/sliding policy against platform absolute/nonrotation proposal. | Refresh, logout, token and expiry implementation. |
| POLICY / MATERIAL | Approve security parameters and protected staging/transfer/cleanup design; benchmark KDF and validate provider behavior. | Verification, secret handling and deployment values. |
| CONSOLIDATION | Produce one active Blueprint/machine/API/schema revision and full applicable 065 result. | Generation readiness and contract compatibility. |
| DEPLOYMENT | Configure delivery provider, KMS, database, service identities, finite recovery/audit budgets, operator grants and on-call route. | Operational rollout. |
| BEHAVIOR | Implement and run R01–R20 plus Session/T16 tests against an identified revision/environment. | Evidence for release, not merely documentary consistency. |

The owner decides architectural/policy options; this plan does not choose an open option by implication. Track each closure in [validation notes](capability.validation_notes.md) with decision reference, exact commit and test evidence. Revalidate pinned platform sources before merge and after any upstream change.
