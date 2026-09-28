# GOV review dossier — ADR-0002 and ADR-0003

Version: 0.1.0 — 2026-09-28 — DRAFT / decision pending

This is a preparation record in SmartCoreIdentity PR #1. The authoritative ADRs and any approval record belong to SmartCorePlatform. This file does not approve, amend, sign or mark either ADR Accepted.

## Pinned review inputs

- SmartCorePlatform `docs/identity-blueprint-completion`, ADR-0002 v1.7.1, blob `3c4a3ac44d605d2cc8f3ffca6de00387fde629fa` ([source](https://github.com/amirfassadi/SmartCorePlatform/blob/docs/identity-blueprint-completion/SmartCore_Platform_Docs_v1/ADR-0002_Identity_Foundation_Clarifications.md)).
- Same branch, ADR-0003 v1.2.1, blob `7eea075bcc23ca90e82b79b0b18246410fde3a18` ([source](https://github.com/amirfassadi/SmartCorePlatform/blob/docs/identity-blueprint-completion/SmartCore_Platform_Docs_v1/ADR-0003_Organization_and_Membership_Lifecycle_Standardization.md)).
- Same branch, normative 051 v1.0, blob `828ea8fb7010015858cacd849b05f8cc074bafbf` ([source](https://github.com/amirfassadi/SmartCorePlatform/blob/docs/identity-blueprint-completion/SmartCore_Platform_Docs_v1/051_SmartCore_Governance_and_Decision_Model.md)).
- ADR-0004 v1.1.1 separately records accepted architectural scope and explicitly leaves ADR-0002/0003 Proposed. Its approval must not be reused as their approval.

Blob SHA identifies file content, not the complete review commit. Before a platform decision, pin the exact candidate commit and review any intervening changes. After merge, record the accepted source commit separately.

## Proposed decision scope for review

| ADR | Relevant decisions for registration | Review conclusion requested |
|---|---|---|
| ADR-0002 | 1 atomic ownership triple; 2 authentication/business-authorization boundary; 3 Owner Membership role; 4 Person-centric v1.x; 5 event ownership and LoginFailed Security Event; 7 RegisterPerson-only Application Service exception; 8 post-commit PendingCredential/Ready and recovery; 9 verified mobile OR email, DisplayName and password | Accept/revise/defer each applicable decision with explicit scope. Decision 6 is future-identity documentation, not a requirement to implement those identities now. Preserve ADR-0004's earlier winner point and acknowledgment protocol. |
| ADR-0003 | Organization and Membership lifecycle models; direct Active creation for MVP, transition Commands deferred | Accept/revise/defer lifecycle scope and direct-Active initialization; do not infer suspend/archive/revoke endpoints. |

An acceptance of only some decisions needs a clear version/scope and a consistent status representation in the platform ADRs; a blanket Accepted label must not conceal unreviewed clauses. The Identity docs must subsequently reference the exact accepted scope and revision.

## Evidence still required by the ADRs

| Evidence | Current dossier result |
|---|---|
| Related 026/027/057/059 and Identity 00–16 narrative/machine/API/service/event synchronization, version references and consumer compatibility | Integrated platform proposal exists; independent architecture/consumer review and merge verification not evidenced here. |
| ADR-0002 verified contact, non-enumeration, proof/replay/material expiry, uniqueness, Outbox and Pending/Ready scenarios | Proposed contracts and R01–R20 test plan exist; runtime/race/security results not evidenced. |
| PersonRegistered Ready timing, OwnershipCommittedAt, optional Email, no initial SessionReference and actual actor; LoginFailed classification | Proposed contracts exist; consumer migration and application tests remain open. |
| ADR-0003 transition terminology across 057, 059, 01, 03 and 14; direct Active MVP semantics | Proposed wording exists; architecture review and structural validation result not evidenced. |
| Architecture Validation Review and Structural Validation under 065 | No complete passing result supplied by this Identity package. |
| Attributable approval, date, scope and immutable platform revision under 051 | Pending owner decision; no signature or acceptance recorded by this file. |

## Governance sequencing question to resolve explicitly

051 §7 orders Approval before Implementation. The acceptance criteria in ADR-0002 also say some proof/replay cases must be *tested* before changing its status, while Identity/13 describes runtime tests requiring an implementation. The architecture owner must distinguish an architectural review/approval gate from later implementation verification, or state what pre-approval evidence satisfies those criteria. Do not silently mark runtime tests passed, change ADR status, or assume this dossier resolves the tension. ADR-0004's scoped acceptance record is a precedent for making approval scope and later verification obligations explicit, not automatic authorization to apply that treatment to ADR-0002/0003.

## Review-ready decision template (unfilled)

- Candidate platform commit:
- Decision authority and capacity:
- ADR-0002 decision numbers accepted / revised / deferred:
- ADR-0003 lifecycle scope accepted / revised / deferred:
- Evidence reviewed, including consumer inventory and architecture/065 results:
- Treatment of pre-approval tests versus post-approval runtime verification:
- Remaining conditions and responsible owner:
- Approval/effective date and attributable record:
- Platform ADR/document changes and accepted commit:
- SmartCoreIdentity contract/source-pin update commit:

All fields remain unfilled until the corresponding evidence and an explicit decision exist. T16 and SESSION are separate gates even if GOV is closed.
